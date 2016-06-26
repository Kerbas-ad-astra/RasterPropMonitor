/*****************************************************************************
 * RasterPropMonitor
 * =================
 * Plugin for Kerbal Space Program
 *
 *  by Mihara (Eugene Medvedev), MOARdV, and other contributors
 * 
 * RasterPropMonitor is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, revision
 * date 29 June 2007, or (at your option) any later version.
 * 
 * RasterPropMonitor is distributed in the hope that it will be useful, but
 * WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
 * or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
 * for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with RasterPropMonitor.  If not, see <http://www.gnu.org/licenses/>.
 ****************************************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JSI
{
    /// <summary>
    /// JSICallbackAnimator is an alternative for JSIVariableAnimator that handles
    /// thresholded behavior (switching on/off, not interpolating between two
    /// states).
    /// </summary>
    public class JSICallbackAnimator : InternalModule
    {
        [KSPField]
        public string variableName = string.Empty;
        [KSPField]
        public float flashRate = 0.0f;

        private readonly List<CallbackAnimationSet> variableSets = new List<CallbackAnimationSet>();
        private Action<float> del;
        private RasterPropMonitorComputer rpmComp;
        private JSIFlashModule fm;

        /// <summary>
        /// Start and initialize all the things!
        /// </summary>
        public void Start()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                return;
            }

            try
            {
                rpmComp = RasterPropMonitorComputer.Instantiate(internalProp, true);

                ConfigNode moduleConfig = null;
                foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("PROP"))
                {
                    if (node.GetValue("name") == internalProp.propName)
                    {
                        if (string.IsNullOrEmpty(variableName))
                        {
                            JUtil.LogErrorMessage(this, "Configuration failed in prop {0} ({1}), no variableName.", internalProp.propID, internalProp.propName);
                            throw new ArgumentNullException();
                        }

                        moduleConfig = node.GetNodes("MODULE")[moduleID];
                        ConfigNode[] variableNodes = moduleConfig.GetNodes("VARIABLESET");

                        for (int i = 0; i < variableNodes.Length; i++)
                        {
                            try
                            {
                                variableSets.Add(new CallbackAnimationSet(variableNodes[i], variableName, internalProp));
                            }
                            catch (ArgumentException e)
                            {
                                JUtil.LogErrorMessage(this, "Error in building prop number {1} - {0}", e.Message, internalProp.propID);
                            }
                        }
                        break;
                    }
                }

                if (flashRate > 0.0f)
                {
                    fm = JUtil.InstallFlashModule(part, flashRate);

                    if (fm != null)
                    {
                        fm.flashSubscribers += FlashToggle;
                    }
                }

                del = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), this, "OnCallback");
                rpmComp.RegisterVariableCallback(variableName, del);
                JUtil.LogMessage(this, "Configuration complete in prop {1} ({2}), supporting {0} callback animators.", variableSets.Count, internalProp.propID, internalProp.propName);
            }
            catch
            {
                JUtil.AnnoyUser(this);
                enabled = false;
                throw;
            }
        }

        /// <summary>
        /// Callback to update flashing parts
        /// </summary>
        /// <param name="newState"></param>
        private void FlashToggle(bool newState)
        {
            for (int i = 0; i < variableSets.Count; ++i)
            {
                variableSets[i].FlashState(newState);
            }
        }

        /// <summary>
        /// Tear down the object.
        /// </summary>
        public void OnDestroy()
        {
            for (int i = 0; i < variableSets.Count; ++i)
            {
                variableSets[i].TearDown();
            }
            variableSets.Clear();

            try
            {
                rpmComp.UnregisterVariableCallback(variableName, del);
                if (fm != null)
                {
                    fm.flashSubscribers -= FlashToggle;
                }
            }
            catch
            {
                //JUtil.LogMessage(this, "Trapped exception unregistering JSICallback (you can ignore this)");
            }
        }

        /// <summary>
        /// Callback RasterPropMonitorComputer calls when the variable of interest
        /// changes.
        /// </summary>
        /// <param name="value"></param>
        void OnCallback(float value)
        {
            // Sanity checks:
            if (vessel == null)
            {
                // Stop getting callbacks if for some reason a different
                // computer is talking to us.
                //JUtil.LogMessage(this, "OnCallback - unregistering del {0}, vessel null is {1}, comp.id = {2}", del.GetHashCode(), (vessel == null), comp.id);
                rpmComp.UnregisterVariableCallback(variableName, del);
                JUtil.LogErrorMessage(this, "Received an unexpected OnCallback()");
            }
            else
            {
                for (int i = 0; i < variableSets.Count; ++i)
                {
                    variableSets[i].Update(value);
                }
            }
        }
    }

    /// <summary>
    /// CallbackAnimationSet tracks one particular animation (color change,
    /// rotation, transformation, texture coordinate change).  It has an
    /// independent range of enabling values, but it depends on the parent
    /// JSICallback class to control what variable is tracked.
    /// </summary>
    public class CallbackAnimationSet
    {
        private readonly VariableOrNumberRange variable;
        private readonly Animation onAnim;
        private readonly Animation offAnim;
        private readonly bool reverse;
        private readonly string animationName;
        private readonly string stopAnimationName;
        private readonly float animationSpeed;
        private readonly Color passiveColor, activeColor;
        private Transform controlledTransform;
        private readonly Vector3 initialPosition, initialScale, vectorStart, vectorEnd;
        private readonly Quaternion initialRotation, rotationStart, rotationEnd;
        private readonly bool longPath;
        private readonly int colorName = -1;
        private readonly Vector2 textureShiftStart, textureShiftEnd, textureScaleStart, textureScaleEnd;
        private Material affectedMaterial;
        private List<string> textureLayer = new List<string>();
        private readonly Mode mode;
        private readonly bool looping;
        private readonly bool flash;
        private FXGroup audioOutput;
        private readonly float alarmSoundVolume;
        private readonly bool alarmMustPlayOnce;
        private readonly bool alarmSoundLooping;
        // runtime values:
        private bool alarmActive; 
        private bool currentState;
        private bool inIVA = false;

        private enum Mode
        {
            Animation,
            Color,
            LoopingAnimation,
            Rotation,
            Translation,
            Scale,
            TextureShift,
            TextureScale,
        }

        /// <summary>
        /// Initialize and configure the callback handler.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="variableName"></param>
        /// <param name="thisProp"></param>
        public CallbackAnimationSet(ConfigNode node, string variableName, InternalProp thisProp)
        {
            currentState = false;

            if (!node.HasData)
            {
                throw new ArgumentException("No data?!");
            }

            string[] tokens = { };

            if (node.HasValue("scale"))
            {
                tokens = node.GetValue("scale").Split(',');
            }

            if (tokens.Length != 2)
            {
                throw new ArgumentException("Could not parse 'scale' parameter.");
            }

            RasterPropMonitorComputer rpmComp = RasterPropMonitorComputer.Instantiate(thisProp, true);
            variable = new VariableOrNumberRange(rpmComp, variableName, tokens[0], tokens[1]);

            // That takes care of the scale, now what to do about that scale:
            if (node.HasValue("reverse"))
            {
                if (!bool.TryParse(node.GetValue("reverse"), out reverse))
                {
                    throw new ArgumentException("So is 'reverse' true or false?");
                }
            }

            if (node.HasValue("flash"))
            {
                if(!bool.TryParse(node.GetValue("flash"), out flash))
                {
                    throw new ArgumentException("So is 'reverse' true or false?");
                }
            }
            else
            {
                flash = false;
            }

            if (node.HasValue("alarmSound"))
            {
                alarmSoundVolume = 0.5f;
                if (node.HasValue("alarmSoundVolume"))
                {
                    alarmSoundVolume = float.Parse(node.GetValue("alarmSoundVolume"));
                }
                audioOutput = JUtil.SetupIVASound(thisProp, node.GetValue("alarmSound"), alarmSoundVolume, false);
                if (node.HasValue("alarmMustPlayOnce"))
                {
                    if (!bool.TryParse(node.GetValue("alarmMustPlayOnce"), out alarmMustPlayOnce))
                    {
                        throw new ArgumentException("So is 'alarmMustPlayOnce' true or false?");
                    }
                }
                if (node.HasValue("alarmShutdownButton"))
                {
                    SmarterButton.CreateButton(thisProp, node.GetValue("alarmShutdownButton"), AlarmShutdown);
                }
                if (node.HasValue("alarmSoundLooping"))
                {
                    if (!bool.TryParse(node.GetValue("alarmSoundLooping"), out alarmSoundLooping))
                    {
                        throw new ArgumentException("So is 'alarmSoundLooping' true or false?");
                    }
                    audioOutput.audio.loop = alarmSoundLooping;
                }

                inIVA = (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA);

                GameEvents.OnCameraChange.Add(CameraChangeCallback);
            }

            if (node.HasValue("animationName"))
            {
                animationName = node.GetValue("animationName");
                if (node.HasValue("animationSpeed"))
                {
                    animationSpeed = float.Parse(node.GetValue("animationSpeed"));

                    if (reverse)
                    {
                        animationSpeed = -animationSpeed;
                    }
                }
                else
                {
                    animationSpeed = 0.0f;
                }
                Animation[] anims = node.HasValue("animateExterior") ? thisProp.part.FindModelAnimators(animationName) : thisProp.FindModelAnimators(animationName);
                if (anims.Length > 0)
                {
                    onAnim = anims[0];
                    onAnim.enabled = true;
                    onAnim[animationName].speed = 0;
                    onAnim[animationName].normalizedTime = reverse ? 1f : 0f;
                    looping = node.HasValue("loopingAnimation");
                    if (looping)
                    {
                        onAnim[animationName].wrapMode = WrapMode.Loop;
                        onAnim.wrapMode = WrapMode.Loop;
                        onAnim[animationName].speed = animationSpeed;
                        mode = Mode.LoopingAnimation;
                    }
                    else
                    {
                        onAnim[animationName].wrapMode = WrapMode.Once;
                        mode = Mode.Animation;
                    }
                    onAnim.Play();
                    //alwaysActive = node.HasValue("animateExterior");
                }
                else
                {
                    throw new ArgumentException("Animation "+ animationName +" could not be found.");
                }

                if (node.HasValue("stopAnimationName"))
                {
                    stopAnimationName = node.GetValue("stopAnimationName");
                    anims = node.HasValue("animateExterior") ? thisProp.part.FindModelAnimators(stopAnimationName) : thisProp.FindModelAnimators(stopAnimationName);
                    if (anims.Length > 0)
                    {
                        offAnim = anims[0];
                        offAnim.enabled = true;
                        offAnim[stopAnimationName].speed = 0;
                        offAnim[stopAnimationName].normalizedTime = reverse ? 1f : 0f;
                        if (looping)
                        {
                            offAnim[stopAnimationName].wrapMode = WrapMode.Loop;
                            offAnim.wrapMode = WrapMode.Loop;
                            offAnim[stopAnimationName].speed = animationSpeed;
                            mode = Mode.LoopingAnimation;
                        }
                        else
                        {
                            offAnim[stopAnimationName].wrapMode = WrapMode.Once;
                            mode = Mode.Animation;
                        }
                    }
                }
            }
            else if (node.HasValue("activeColor") && node.HasValue("passiveColor") && node.HasValue("coloredObject"))
            {
                string colorNameString = "_EmissiveColor";
                if (node.HasValue("colorName"))
                {
                    colorNameString = node.GetValue("colorName");
                }
                colorName = Shader.PropertyToID(colorNameString);

                if (reverse)
                {
                    activeColor = JUtil.ParseColor32(node.GetValue("passiveColor"), thisProp.part, ref rpmComp);
                    passiveColor = JUtil.ParseColor32(node.GetValue("activeColor"), thisProp.part, ref rpmComp);
                }
                else
                {
                    passiveColor = JUtil.ParseColor32(node.GetValue("passiveColor"), thisProp.part, ref rpmComp);
                    activeColor = JUtil.ParseColor32(node.GetValue("activeColor"), thisProp.part, ref rpmComp);
                }
                Renderer colorShiftRenderer = thisProp.FindModelComponent<Renderer>(node.GetValue("coloredObject"));
                affectedMaterial = colorShiftRenderer.material;
                affectedMaterial.SetColor(colorName, passiveColor);
                mode = Mode.Color;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("localRotationStart") && node.HasValue("localRotationEnd"))
            {
                controlledTransform = thisProp.FindModelTransform(node.GetValue("controlledTransform").Trim());
                initialRotation = controlledTransform.localRotation;
                if (node.HasValue("longPath"))
                {
                    longPath = true;
                    if (reverse)
                    {
                        vectorEnd = ConfigNode.ParseVector3(node.GetValue("localRotationStart"));
                        vectorStart = ConfigNode.ParseVector3(node.GetValue("localRotationEnd"));
                    }
                    else
                    {
                        vectorStart = ConfigNode.ParseVector3(node.GetValue("localRotationStart"));
                        vectorEnd = ConfigNode.ParseVector3(node.GetValue("localRotationEnd"));
                    }
                }
                else
                {
                    if (reverse)
                    {
                        rotationEnd = Quaternion.Euler(ConfigNode.ParseVector3(node.GetValue("localRotationStart")));
                        rotationStart = Quaternion.Euler(ConfigNode.ParseVector3(node.GetValue("localRotationEnd")));
                    }
                    else
                    {
                        rotationStart = Quaternion.Euler(ConfigNode.ParseVector3(node.GetValue("localRotationStart")));
                        rotationEnd = Quaternion.Euler(ConfigNode.ParseVector3(node.GetValue("localRotationEnd")));
                    }
                }
                mode = Mode.Rotation;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("localTranslationStart") && node.HasValue("localTranslationEnd"))
            {
                controlledTransform = thisProp.FindModelTransform(node.GetValue("controlledTransform").Trim());
                initialPosition = controlledTransform.localPosition;
                if (reverse)
                {
                    vectorEnd = ConfigNode.ParseVector3(node.GetValue("localTranslationStart"));
                    vectorStart = ConfigNode.ParseVector3(node.GetValue("localTranslationEnd"));
                }
                else
                {
                    vectorStart = ConfigNode.ParseVector3(node.GetValue("localTranslationStart"));
                    vectorEnd = ConfigNode.ParseVector3(node.GetValue("localTranslationEnd"));
                }
                mode = Mode.Translation;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("localScaleStart") && node.HasValue("localScaleEnd"))
            {
                controlledTransform = thisProp.FindModelTransform(node.GetValue("controlledTransform").Trim());
                initialScale = controlledTransform.localScale;
                if (reverse)
                {
                    vectorEnd = ConfigNode.ParseVector3(node.GetValue("localScaleStart"));
                    vectorStart = ConfigNode.ParseVector3(node.GetValue("localScaleEnd"));
                }
                else
                {
                    vectorStart = ConfigNode.ParseVector3(node.GetValue("localScaleStart"));
                    vectorEnd = ConfigNode.ParseVector3(node.GetValue("localScaleEnd"));
                }
                mode = Mode.Scale;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("textureLayers") && node.HasValue("textureShiftStart") && node.HasValue("textureShiftEnd"))
            {
                affectedMaterial = thisProp.FindModelTransform(node.GetValue("controlledTransform").Trim()).GetComponent<Renderer>().material;
                var textureLayers = node.GetValue("textureLayers").Split(',');
                for (int i = 0; i < textureLayers.Length; ++i)
                {
                    textureLayer.Add(textureLayers[i].Trim());
                }

                if (reverse)
                {
                    textureShiftEnd = ConfigNode.ParseVector2(node.GetValue("textureShiftStart"));
                    textureShiftStart = ConfigNode.ParseVector2(node.GetValue("textureShiftEnd"));
                }
                else
                {
                    textureShiftStart = ConfigNode.ParseVector2(node.GetValue("textureShiftStart"));
                    textureShiftEnd = ConfigNode.ParseVector2(node.GetValue("textureShiftEnd"));
                }
                mode = Mode.TextureShift;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("textureLayers") && node.HasValue("textureScaleStart") && node.HasValue("textureScaleEnd"))
            {
                affectedMaterial = thisProp.FindModelTransform(node.GetValue("controlledTransform").Trim()).GetComponent<Renderer>().material;
                var textureLayers = node.GetValue("textureLayers").Split(',');
                for (int i = 0; i < textureLayers.Length; ++i)
                {
                    textureLayer.Add(textureLayers[i].Trim());
                }

                if (reverse)
                {
                    textureScaleEnd = ConfigNode.ParseVector2(node.GetValue("textureScaleStart"));
                    textureScaleStart = ConfigNode.ParseVector2(node.GetValue("textureScaleEnd"));
                }
                else
                {
                    textureScaleStart = ConfigNode.ParseVector2(node.GetValue("textureScaleStart"));
                    textureScaleEnd = ConfigNode.ParseVector2(node.GetValue("textureScaleEnd"));
                }
                mode = Mode.TextureScale;
            }
            else
            {
                throw new ArgumentException("Cannot initiate any of the possible action modes.");
            }

            TurnOff();
        }

        /// <summary>
        /// Callback method to notify animators that flash that it's time to flash.
        /// </summary>
        /// <param name="toggleState"></param>
        internal void FlashState(bool toggleState)
        {
            if (flash && currentState)
            {
                if (toggleState)
                {
                    TurnOn();
                }
                else
                {
                    TurnOff();
                }
            }
        }

        /// <summary>
        /// Some things need to be explicitly destroyed due to Unity quirks. 
        /// </summary>
        internal void TearDown()
        {
            if (audioOutput != null)
            {
                GameEvents.OnCameraChange.Remove(CameraChangeCallback);
            }
            if (affectedMaterial != null)
            {
                UnityEngine.Object.Destroy(affectedMaterial);
                affectedMaterial = null;
            }
            textureLayer = null;
            controlledTransform = null;
        }

        /// <summary>
        /// Switch the animator to the ON state.
        /// </summary>
        private void TurnOn()
        {
            switch (mode)
            {
                case Mode.Color:
                    affectedMaterial.SetColor(colorName, activeColor);
                    break;
                case Mode.Animation:
                    onAnim[animationName].normalizedTime = reverse ? 0f : 1f;
                    break;
                case Mode.LoopingAnimation:
                    onAnim[animationName].speed = animationSpeed;
                    if (!onAnim.IsPlaying(animationName))
                    {
                        onAnim.Play(animationName);
                    }
                    break;
                case Mode.Rotation:
                    controlledTransform.localRotation = initialRotation * (longPath ? Quaternion.Euler(vectorEnd) : rotationEnd);
                    break;
                case Mode.Translation:
                    controlledTransform.localPosition = initialPosition + vectorEnd;
                    break;
                case Mode.Scale:
                    controlledTransform.localScale = initialScale + vectorEnd;
                    break;
                case Mode.TextureShift:
                    for (int i = 0; i < textureLayer.Count; ++i)
                    {
                        affectedMaterial.SetTextureOffset(textureLayer[i], textureShiftEnd);
                    }
                    break;
                case Mode.TextureScale:
                    for (int i = 0; i < textureLayer.Count; ++i)
                    {
                        affectedMaterial.SetTextureScale(textureLayer[i], textureScaleEnd);
                    }
                    break;
            }
        }

        /// <summary>
        /// Switch the animator to the OFF state
        /// </summary>
        private void TurnOff()
        {
            switch (mode)
            {
                case Mode.Color:
                    affectedMaterial.SetColor(colorName, passiveColor);
                    break;
                case Mode.Animation:
                    onAnim[animationName].normalizedTime = reverse ? 1f : 0f;
                    break;
                case Mode.LoopingAnimation:
                    if (offAnim != null)
                    {
                        offAnim[stopAnimationName].speed = animationSpeed;
                        if (!offAnim.IsPlaying(stopAnimationName))
                        {
                            offAnim.Play(stopAnimationName);
                        }
                    }
                    else
                    {
                        onAnim[animationName].speed = 0.0f;
                        onAnim[animationName].normalizedTime = reverse ? 1f : 0f;
                    }
                    break;
                case Mode.Rotation:
                    controlledTransform.localRotation = initialRotation * (longPath ? Quaternion.Euler(vectorStart) : rotationStart);
                    break;
                case Mode.Translation:
                    controlledTransform.localPosition = initialPosition + vectorStart;
                    break;
                case Mode.Scale:
                    controlledTransform.localScale = initialScale + vectorStart;
                    break;
                case Mode.TextureShift:
                    for (int i = 0; i < textureLayer.Count; ++i)
                    {
                        affectedMaterial.SetTextureOffset(textureLayer[i], textureShiftStart);
                    }
                    break;
                case Mode.TextureScale:
                    for (int i = 0; i < textureLayer.Count; ++i)
                    {
                        affectedMaterial.SetTextureScale(textureLayer[i], textureScaleStart);
                    }
                    break;
            }
        }

        /// <summary>
        /// Receive an update on the value; test if it is in the range we care
        /// about, do what's appropriate if it is.
        /// </summary>
        /// <param name="value"></param>
        public void Update(float value)
        {
            bool newState = variable.IsInRange(value);

            if (newState ^ currentState)
            {
                // State has changed
                if (newState)
                {
                    TurnOn();

                    if (audioOutput != null && !alarmActive)
                    {
                        audioOutput.audio.volume = (inIVA) ? alarmSoundVolume * GameSettings.SHIP_VOLUME : 0.0f;
                        audioOutput.audio.Play();
                        alarmActive = true;
                    }
                }
                else
                {
                    TurnOff();

                    if (audioOutput != null && alarmActive)
                    {
                        if (!alarmMustPlayOnce)
                        {
                            audioOutput.audio.Stop();
                        }
                        alarmActive = false;
                    }
                }

                currentState = newState;
            }
        }

        /// <summary>
        /// Callback to handle when the camera is switched from IVA to flight
        /// </summary>
        /// <param name="newMode"></param>
        public void CameraChangeCallback(CameraManager.CameraMode newMode)
        {
            JUtil.LogMessage(this, "CameraChangeCallback({0})", newMode);
            inIVA = (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA);

            if(inIVA)
            {
                if (audioOutput != null && alarmActive)
                {
                    audioOutput.audio.volume = alarmSoundVolume * GameSettings.SHIP_VOLUME;
                }
            }
            else
            {
                if (audioOutput != null && alarmActive)
                {
                    audioOutput.audio.volume = 0.0f;
                }
            }
        }

        /// <summary>
        /// Callback to turn off an alarm in response to a button hit on the prop.
        /// </summary>
        public void AlarmShutdown()
        {
            if (audioOutput != null && alarmActive && audioOutput.audio.isPlaying)
            {
                audioOutput.audio.Stop();
            }
        }
    }
}

