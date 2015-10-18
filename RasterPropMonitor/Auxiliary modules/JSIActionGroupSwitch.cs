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
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using System.Globalization;

namespace JSI
{
    internal class JSIActionGroupSwitch : InternalModule
    {
        [KSPField]
        public string animationName = string.Empty;
        [KSPField]
        public bool animateExterior = false;
        [KSPField]
        public string switchTransform = string.Empty;
        [KSPField]
        public string actionName = "lights";
        [KSPField]
        public string perPodPersistenceName = string.Empty;
        [KSPField]
        public string perPodMasterSwitchName = string.Empty;
        [KSPField]
        public string masterVariableName = string.Empty;
        private VariableOrNumberRange masterVariable = null;
        [KSPField]
        public string masterVariableRange = string.Empty;
        [KSPField]
        public bool reverse = false;
        [KSPField]
        public float customSpeed = 1f;
        [KSPField]
        public string internalLightName = string.Empty;
        [KSPField]
        public string needsElectricCharge = string.Empty;
        private bool needsElectricChargeValue;
        [KSPField]
        public string resourceName = "SYSR_ELECTRICCHARGE";
        [KSPField]
        public string switchSound = "Squad/Sounds/sound_click_flick";
        [KSPField]
        public float switchSoundVolume = 0.5f;
        [KSPField]
        public string loopingSound = string.Empty;
        [KSPField]
        public float loopingSoundVolume = 0.0f;
        [KSPField]
        public string coloredObject = string.Empty;
        [KSPField]
        public string colorName = "_EmissiveColor";
        [KSPField]
        public string consumeOnToggle = string.Empty;
        [KSPField]
        public string consumeWhileActive = string.Empty;
        [KSPField]
        public string disabledColor = string.Empty;
        private Color disabledColorValue;
        [KSPField]
        public string enabledColor = string.Empty;
        private Color enabledColorValue;
        [KSPField]
        public bool initialState = false;
        [KSPField]
        public int switchGroupIdentifier = -1;
        [KSPField]
        public int refreshRate = 60;
        // Neater.
        internal static readonly Dictionary<string, KSPActionGroup> groupList = new Dictionary<string, KSPActionGroup> { 
			{ "gear",KSPActionGroup.Gear },
			{ "brakes",KSPActionGroup.Brakes },
			{ "lights",KSPActionGroup.Light },
			{ "rcs",KSPActionGroup.RCS },
			{ "sas",KSPActionGroup.SAS },
			{ "abort",KSPActionGroup.Abort },
			{ "stage",KSPActionGroup.Stage },
			{ "custom01",KSPActionGroup.Custom01 },
			{ "custom02",KSPActionGroup.Custom02 },
			{ "custom03",KSPActionGroup.Custom03 },
			{ "custom04",KSPActionGroup.Custom04 },
			{ "custom05",KSPActionGroup.Custom05 },
			{ "custom06",KSPActionGroup.Custom06 },
			{ "custom07",KSPActionGroup.Custom07 },
			{ "custom08",KSPActionGroup.Custom08 },
			{ "custom09",KSPActionGroup.Custom09 },
			{ "custom10",KSPActionGroup.Custom10 }
		};
        internal enum CustomActions
        {
            None,
            IntLight,
            Dummy,
            Plugin,
            Stage,
            Transfer
        };
        private bool customGroupState = false;
        internal static readonly Dictionary<string, CustomActions> customGroupList = new Dictionary<string, CustomActions> {
            { "---none---", CustomActions.None},
            { "intlight", CustomActions.IntLight },
            { "dummy",CustomActions.Dummy },
            { "plugin",CustomActions.Plugin },
            { "transfer", CustomActions.Transfer},
            { "stage",CustomActions.Stage }
        };
        private KSPActionGroup kspAction = KSPActionGroup.None;
        private CustomActions customAction = CustomActions.None;
        private Animation anim;
        private bool currentState;
        private bool isCustomAction;
        private string persistentVarName;
        private Light[] lightObjects;
        private FXGroup audioOutput;
        private FXGroup loopingOutput;
        private int lightCheckCountdown;
        private RasterPropMonitorComputer rpmComp;
        private bool startupComplete;
        private Renderer colorShiftRenderer;
        private string stateVariable = string.Empty;
        private Action<bool> actionHandler;
        private bool isPluginAction;

        private string transferGetter = string.Empty;
        private Action<double> transferSetter;
        private string transferPersistentName;

        // Consume-on-toggle and consume-while-active
        private bool consumingOnToggleUp, consumingOnToggleDown;
        private string consumeOnToggleName = string.Empty;
        private float consumeOnToggleAmount;
        private bool consumingWhileActive;
        private string consumeWhileActiveName = string.Empty;
        private float consumeWhileActiveAmount;
        private bool forcedShutdown;

        public void Start()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                return;
            }

            try
            {
                RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);

                if (!groupList.ContainsKey(actionName) && !customGroupList.ContainsKey(actionName))
                {
                    JUtil.LogErrorMessage(this, "Action \"{0}\" is not supported.", actionName);
                    return;
                }

                // Parse the needs-electric-charge here.
                if (!string.IsNullOrEmpty(needsElectricCharge))
                {
                    switch (needsElectricCharge.ToLowerInvariant().Trim())
                    {
                        case "true":
                        case "yes":
                        case "1":
                            needsElectricChargeValue = true;
                            break;
                        case "false":
                        case "no":
                        case "0":
                            needsElectricChargeValue = false;
                            break;
                    }
                }

                // Now parse consumeOnToggle and consumeWhileActive...
                if (!string.IsNullOrEmpty(consumeOnToggle))
                {
                    string[] tokens = consumeOnToggle.Split(',');
                    if (tokens.Length == 3)
                    {
                        consumeOnToggleName = tokens[0].Trim();
                        if (!(PartResourceLibrary.Instance.GetDefinition(consumeOnToggleName) != null &&
                           float.TryParse(tokens[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture,
                               out consumeOnToggleAmount)))
                        {
                            JUtil.LogErrorMessage(this, "Could not parse \"{0}\"", consumeOnToggle);
                        }
                        switch (tokens[2].Trim().ToLower())
                        {
                            case "on":
                                consumingOnToggleUp = true;
                                break;
                            case "off":
                                consumingOnToggleDown = true;
                                break;
                            case "both":
                                consumingOnToggleUp = true;
                                consumingOnToggleDown = true;
                                break;
                            default:
                                JUtil.LogErrorMessage(this, "So should I consume resources when turning on, turning off, or both in \"{0}\"?", consumeOnToggle);
                                break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(consumeWhileActive))
                {
                    string[] tokens = consumeWhileActive.Split(',');
                    if (tokens.Length == 2)
                    {
                        consumeWhileActiveName = tokens[0].Trim();
                        if (!(PartResourceLibrary.Instance.GetDefinition(consumeWhileActiveName) != null &&
                           float.TryParse(tokens[1].Trim(),
                               NumberStyles.Any, CultureInfo.InvariantCulture,
                               out consumeWhileActiveAmount)))
                        {
                            JUtil.LogErrorMessage(this, "Could not parse \"{0}\"", consumeWhileActive);
                        }
                        else
                        {
                            consumingWhileActive = true;
                            JUtil.LogMessage(this, "Switch in prop {0} prop id {1} will consume {2} while active at a rate of {3}", internalProp.propName,
                                internalProp.propID, consumeWhileActiveName, consumeWhileActiveAmount);
                        }
                    }
                }

                if (groupList.ContainsKey(actionName))
                {
                    kspAction = groupList[actionName];
                    currentState = vessel.ActionGroups[kspAction];
                    // action group switches may not belong to a radio group
                    switchGroupIdentifier = -1;
                }
                else
                {
                    isCustomAction = true;
                    switch (actionName)
                    {
                        case "intlight":
                            persistentVarName = internalLightName;
                            lightObjects = internalModel.FindModelComponents<Light>();
                            needsElectricChargeValue |= string.IsNullOrEmpty(needsElectricCharge) || needsElectricChargeValue;
                            break;
                        case "plugin":
                            persistentVarName = string.Empty;
                            comp.UpdateDataRefreshRate(refreshRate);

                            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("PROP"))
                            {
                                if (node.GetValue("name") == internalProp.propName)
                                {
                                    foreach (ConfigNode pluginConfig in node.GetNodes("MODULE")[moduleID].GetNodes("PLUGINACTION"))
                                    {
                                        if (pluginConfig.HasValue("name") && pluginConfig.HasValue("actionMethod"))
                                        {
                                            string action = pluginConfig.GetValue("name").Trim() + ":" + pluginConfig.GetValue("actionMethod").Trim();
                                            actionHandler = (Action<bool>)comp.GetMethod(action, internalProp, typeof(Action<bool>));

                                            if (actionHandler == null)
                                            {
                                                JUtil.LogErrorMessage(this, "Failed to instantiate action handler {0}", action);
                                            }
                                            else
                                            {
                                                if (pluginConfig.HasValue("stateMethod"))
                                                {
                                                    string state = pluginConfig.GetValue("name").Trim() + ":" + pluginConfig.GetValue("stateMethod").Trim();
                                                    stateVariable = "PLUGIN_" + state;
                                                }
                                                else if (pluginConfig.HasValue("stateVariable"))
                                                {
                                                    stateVariable = pluginConfig.GetValue("stateVariable").Trim();
                                                }
                                                isPluginAction = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            if (actionHandler == null)
                            {
                                actionName = "dummy";
                                JUtil.LogMessage(this, "Plugin handlers did not start, reverting to dummy mode.");
                            }
                            break;
                        case "transfer":
                            persistentVarName = string.Empty;
                            comp.UpdateDataRefreshRate(refreshRate);

                            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("PROP"))
                            {
                                if (node.GetValue("name") == internalProp.propName)
                                {
                                    foreach (ConfigNode pluginConfig in node.GetNodes("MODULE")[moduleID].GetNodes("TRANSFERACTION"))
                                    {
                                        if ((pluginConfig.HasValue("name") || pluginConfig.HasValue("getVariable")) && pluginConfig.HasValue("perPodPersistenceName"))
                                        {
                                            transferPersistentName = pluginConfig.GetValue("perPodPersistenceName").Trim();
                                            if (pluginConfig.HasValue("stateMethod"))
                                            {
                                                string state = pluginConfig.GetValue("name").Trim() + ":" + pluginConfig.GetValue("stateMethod").Trim();
                                                stateVariable = "PLUGIN_" + state;
                                            }
                                            else if (pluginConfig.HasValue("stateVariable"))
                                            {
                                                stateVariable = pluginConfig.GetValue("stateVariable").Trim();
                                            }
                                            if (pluginConfig.HasValue("setMethod"))
                                            {
                                                string action = pluginConfig.GetValue("name").Trim() + ":" + pluginConfig.GetValue("setMethod").Trim();
                                                transferSetter = (Action<double>)comp.GetMethod(action, internalProp, typeof(Action<double>));

                                                if (transferSetter == null)
                                                {
                                                    JUtil.LogErrorMessage(this, "Failed to instantiate transfer handler {0}", pluginConfig.GetValue("name"));
                                                }
                                                else
                                                {
                                                    //JUtil.LogMessage(this, "Got setter {0}", action);
                                                    break;
                                                }
                                            }
                                            else if (pluginConfig.HasValue("getMethod"))
                                            {
                                                string action = pluginConfig.GetValue("name").Trim() + ":" + pluginConfig.GetValue("getMethod").Trim();
                                                var getter = (Func<double>)comp.GetMethod(action, internalProp, typeof(Func<double>));

                                                if (getter == null)
                                                {
                                                    JUtil.LogErrorMessage(this, "Failed to instantiate transfer handler {0}", pluginConfig.GetValue("name"));
                                                }
                                                else
                                                {
                                                    transferGetter = "PLUGIN_" + action;
                                                    //JUtil.LogMessage(this, "Got getter {0}", action);
                                                    break;
                                                }
                                            }
                                            else if (pluginConfig.HasValue("getVariable"))
                                            {
                                                transferGetter = pluginConfig.GetValue("getVariable").Trim();
                                            }
                                        }
                                    }
                                }
                            }
                            if (string.IsNullOrEmpty(transferGetter) && transferSetter == null)
                            {
                                actionName = "dummy";
                                stateVariable = string.Empty;
                                JUtil.LogMessage(this, "Transfer handlers did not start, reverting to dummy mode.");
                            }
                            break;
                        default:
                            persistentVarName = "switch" + internalProp.propID + "_" + moduleID;
                            break;
                    }
                    if (!string.IsNullOrEmpty(perPodPersistenceName))
                    {
                        persistentVarName = perPodPersistenceName;
                    }
                    else
                    {
                        // If there's no persistence name, there's no valid group id for this switch
                        switchGroupIdentifier = -1;
                    }
                }

                if (customGroupList.ContainsKey(actionName))
                {
                    customAction = customGroupList[actionName];
                }

                if (needsElectricChargeValue || !string.IsNullOrEmpty(persistentVarName) || !string.IsNullOrEmpty(perPodMasterSwitchName) || !string.IsNullOrEmpty(masterVariableName) ||
                    !string.IsNullOrEmpty(transferGetter) || transferSetter != null)
                {
                    rpmComp = RasterPropMonitorComputer.Instantiate(internalProp);

                    comp.UpdateDataRefreshRate(refreshRate);

                    if (!string.IsNullOrEmpty(masterVariableName))
                    {
                        string[] range = masterVariableRange.Split(',');
                        if (range.Length == 2)
                        {
                            masterVariable = new VariableOrNumberRange(masterVariableName, range[0], range[1]);
                        }
                        else
                        {
                            masterVariable = null;
                        }
                    }
                }

                // set up the toggle switch
                if (!string.IsNullOrEmpty(switchTransform))
                {
                    SmarterButton.CreateButton(internalProp, switchTransform, Click);
                }

                if (isCustomAction)
                {
                    if (isPluginAction && !string.IsNullOrEmpty(stateVariable))
                    {
                        try
                        {
                            currentState = (comp.ProcessVariable(stateVariable, -1).MassageToInt()) > 0;
                        }
                        catch
                        {
                            // no-op
                        }
                    }
                    else
                    {
                        if (rpmComp != null && !string.IsNullOrEmpty(persistentVarName))
                        {
                            if (switchGroupIdentifier >= 0)
                            {
                                int activeSwitch = rpmComp.GetVar(persistentVarName, 0);

                                currentState = customGroupState = (switchGroupIdentifier == activeSwitch);
                            }
                            else
                            {
                                currentState = customGroupState = rpmComp.GetBool(persistentVarName, initialState);
                            }

                            if (customAction == CustomActions.IntLight)
                            {
                                // We have to restore lighting after reading the
                                // persistent variable.
                                SetInternalLights(customGroupState);
                            }
                        }
                    }
                }

                if (rpmComp != null && !rpmComp.HasVar(persistentVarName))
                {
                    if (switchGroupIdentifier >= 0)
                    {
                        if (currentState)
                        {
                            rpmComp.SetVar(persistentVarName, switchGroupIdentifier);
                        }
                    }
                    else
                    {
                        rpmComp.SetVar(persistentVarName, currentState);
                    }
                }

                if (!string.IsNullOrEmpty(animationName))
                {
                    // Set up the animation
                    Animation[] animators = animateExterior ? part.FindModelAnimators(animationName) : internalProp.FindModelAnimators(animationName);
                    if (animators.Length > 0)
                    {
                        anim = animators[0];
                    }
                    else
                    {
                        JUtil.LogErrorMessage(this, "Could not find animation \"{0}\" on {2} \"{1}\"",
                            animationName, animateExterior ? part.name : internalProp.name, animateExterior ? "part" : "prop");
                        return;
                    }
                    anim[animationName].wrapMode = WrapMode.Once;

                    if (currentState ^ reverse)
                    {
                        anim[animationName].speed = float.MaxValue;
                        anim[animationName].normalizedTime = 0;

                    }
                    else
                    {
                        anim[animationName].speed = float.MinValue;
                        anim[animationName].normalizedTime = 1;
                    }
                    anim.Play(animationName);
                }
                else if (!string.IsNullOrEmpty(coloredObject))
                {
                    // Set up the color shift.
                    colorShiftRenderer = internalProp.FindModelComponent<Renderer>(coloredObject);
                    disabledColorValue = ConfigNode.ParseColor32(disabledColor);
                    enabledColorValue = ConfigNode.ParseColor32(enabledColor);
                    colorShiftRenderer.material.SetColor(colorName, (currentState ^ reverse ? enabledColorValue : disabledColorValue));
                }
                else
                {
                    JUtil.LogMessage(this, "Warning, neither color nor animation are defined in prop {0} #{1} (this may be okay).", internalProp.propName, internalProp.propID);
                }

                audioOutput = JUtil.SetupIVASound(internalProp, switchSound, switchSoundVolume, false);

                if (!string.IsNullOrEmpty(loopingSound) && loopingSoundVolume > 0.0f)
                {
                    loopingOutput = JUtil.SetupIVASound(internalProp, loopingSound, loopingSoundVolume, true);
                }

                startupComplete = true;
            }
            catch
            {
                JUtil.AnnoyUser(this);
                enabled = false;
                throw;
            }
        }

        public void OnDestroy()
        {
            //JUtil.LogMessage(this, "OnDestroy()");
            rpmComp = null;
        }

        private void SetInternalLights(bool value)
        {
            foreach (Light lightobject in lightObjects)
            {
                // I probably shouldn't filter them every time, but I am getting
                // serously confused by this hierarchy.
                if (lightobject.name == internalLightName)
                {
                    lightobject.enabled = value;
                }
            }
        }

        public void Click()
        {
            bool switchEnabled = true;
            if (!forcedShutdown)
            {
                if (!string.IsNullOrEmpty(perPodMasterSwitchName))
                {
                    switchEnabled = rpmComp.GetBool(perPodMasterSwitchName, false);
                }
                if (masterVariable != null)
                {
                    RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
                    switchEnabled = masterVariable.IsInRange(comp);
                }
            }
            if (!switchEnabled)
            {
                // If the master switch is 'off' and we're not here because
                // of a forced shutdown, don't allow this switch to work.
                // early return
                return;
            }

            if (isCustomAction)
            {
                if (switchGroupIdentifier >= 0)
                {
                    if (!forcedShutdown && !customGroupState)
                    {
                        customGroupState = true;
                        if (!string.IsNullOrEmpty(persistentVarName))
                        {
                            rpmComp.SetVar(persistentVarName, switchGroupIdentifier);
                        }
                    }
                    // else: can't turn off a radio group switch.
                }
                else if (customAction == CustomActions.Plugin && !string.IsNullOrEmpty(stateVariable))
                {
                    RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
                    int ivalue = comp.ProcessVariable(stateVariable, -1).MassageToInt();
                    customGroupState = (ivalue < 1) && !forcedShutdown;
                }
                else
                {
                    customGroupState = !customGroupState;
                    if (!string.IsNullOrEmpty(persistentVarName))
                    {
                        rpmComp.SetVar(persistentVarName, customGroupState);
                    }
                }
            }
            else
            {
                vessel.ActionGroups.ToggleGroup(kspAction);
            }
            // Now we do extra things that with regular actions can't happen.
            switch (customAction)
            {
                case CustomActions.IntLight:
                    SetInternalLights(customGroupState);
                    break;
                case CustomActions.Plugin:
                    actionHandler(customGroupState);
                    break;
                case CustomActions.Stage:
                    if (InputLockManager.IsUnlocked(ControlTypes.STAGING))
                    {
                        Staging.ActivateNextStage();
                    }
                    break;
                case CustomActions.Transfer:
                    if (!string.IsNullOrEmpty(stateVariable))
                    {
                        // stateVariable can disable the button functionality.
                        RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
                        int ivalue = comp.ProcessVariable(stateVariable, -1).MassageToInt();
                        if (ivalue < 1)
                        {
                            return; // early - button disabled
                        }
                    }
                    if (!string.IsNullOrEmpty(transferGetter))
                    {
                        RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
                        float value = comp.ProcessVariable(transferGetter, internalProp.propID).MassageToFloat();
                        rpmComp.SetVar(transferPersistentName, (int)value);
                    }
                    else if (rpmComp.HasVar(transferPersistentName))
                    {
                        transferSetter((double)rpmComp.GetVar(transferPersistentName));
                    }
                    break;
            }
        }

        public override void OnUpdate()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                return;
            }

            if (!startupComplete)
            {
                return;
            }

            if (!JUtil.IsActiveVessel(vessel))
            {
                if (loopingOutput != null && currentState == true)
                {
                    loopingOutput.audio.volume = 0.0f;
                }
                return;
            }
            else if (loopingOutput != null && currentState == true && loopingOutput.Active)
            {
                loopingOutput.audio.volume = loopingSoundVolume * GameSettings.SHIP_VOLUME;
            }

            if (consumingWhileActive && currentState && !forcedShutdown)
            {
                float requesting = (consumeWhileActiveAmount * TimeWarp.deltaTime);
                float extracted = part.RequestResource(consumeWhileActiveName, requesting);
                if (Math.Abs(extracted - requesting) > Math.Abs(requesting / 2))
                {
                    // We don't have enough of the resource or can't produce more negative resource, so we should shut down...
                    forcedShutdown = true;
                    JUtil.LogMessage(this, "Could not consume {0}, asked for {1}, got {2} shutting switch down.", consumeWhileActiveName, requesting, extracted);
                }
            }

            // Bizarre, but looks like I need to animate things offscreen if I want them in the right condition when camera comes back.
            // So there's no check for internal cameras.

            bool newState;
            if (isPluginAction && !string.IsNullOrEmpty(stateVariable))
            {
                try
                {
                    RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
                    newState = (comp.ProcessVariable(stateVariable, -1).MassageToInt()) > 0;
                }
                catch
                {
                    newState = currentState;
                }
            }
            else if (isCustomAction)
            {
                if (string.IsNullOrEmpty(switchTransform) && !string.IsNullOrEmpty(perPodPersistenceName))
                {
                    if (switchGroupIdentifier >= 0)
                    {
                        int activeGroupId = rpmComp.GetVar(persistentVarName, 0);
                        newState = (switchGroupIdentifier == activeGroupId);
                        customGroupState = newState;
                    }
                    else
                    {
                        // If the switch transform is not given, and the global comp.Persistence value is, this means this is a slave module.
                        newState = rpmComp.GetBool(persistentVarName, false);
                    }
                }
                else
                {
                    // Otherwise it's a master module. But it still might have to follow the clicks on other copies of the same prop...
                    if (!string.IsNullOrEmpty(perPodPersistenceName))
                    {
                        if (switchGroupIdentifier >= 0)
                        {
                            int activeGroupId = rpmComp.GetVar(persistentVarName, 0);
                            newState = (switchGroupIdentifier == activeGroupId);
                            customGroupState = newState;
                        }
                        else
                        {
                            newState = rpmComp.GetBool(persistentVarName, customGroupState);
                        }
                    }
                    else
                    {
                        newState = customGroupState;
                    }
                }
            }
            else
            {
                newState = vessel.ActionGroups[kspAction];
            }

            // If needsElectricCharge is true and there is no charge, the state value is overridden to false and the click action is reexecuted.
            if (needsElectricChargeValue)
            {
                lightCheckCountdown--;
                if (lightCheckCountdown <= 0)
                {
                    RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
                    lightCheckCountdown = refreshRate;
                    forcedShutdown |= currentState && comp.ProcessVariable(resourceName).MassageToFloat() < 0.01f;
                }
            }

            if (!string.IsNullOrEmpty(perPodMasterSwitchName))
            {
                bool switchEnabled = rpmComp.GetBool(perPodMasterSwitchName, false);
                if (!switchEnabled)
                {
                    // If the master switch is 'off', this switch needs to turn off
                    newState = false;
                    forcedShutdown = true;
                }
            }

            if (masterVariable != null)
            {
                RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
                if (!masterVariable.IsInRange(comp))
                {
                    newState = false;
                    forcedShutdown = true;
                }
            }

            if (forcedShutdown)
            {
                if (currentState)
                {
                    Click();
                }
                newState = false;
                forcedShutdown = false;
            }

            if (newState != currentState)
            {
                // If we're consuming resources on toggle, do that now.
                if ((consumingOnToggleUp && newState) || (consumingOnToggleDown && !newState))
                {
                    float extracted = part.RequestResource(consumeOnToggleName, consumeOnToggleAmount);
                    if (Math.Abs(extracted - consumeOnToggleAmount) > Math.Abs(consumeOnToggleAmount / 2))
                    {
                        // We don't have enough of the resource, so we force a shutdown on the next loop.
                        // This ensures the animations will play at least once.
                        forcedShutdown = true;
                    }
                }

                if (audioOutput != null && (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA ||
                    CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Internal))
                {
                    audioOutput.audio.Play();
                }

                if (loopingOutput != null && (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA ||
                    CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Internal))
                {
                    if (newState)
                    {
                        loopingOutput.audio.Play();
                    }
                    else
                    {
                        loopingOutput.audio.Stop();
                    }
                }

                if (anim != null)
                {
                    if (newState ^ reverse)
                    {
                        anim[animationName].normalizedTime = 0;
                        anim[animationName].speed = 1f * customSpeed;
                        anim.Play(animationName);
                    }
                    else
                    {
                        anim[animationName].normalizedTime = 1;
                        anim[animationName].speed = -1f * customSpeed;
                        anim.Play(animationName);
                    }
                }
                else if (colorShiftRenderer != null)
                {
                    colorShiftRenderer.material.SetColor(colorName, (newState ^ reverse ? enabledColorValue : disabledColorValue));
                }
                currentState = newState;
            }
        }

    }
}
