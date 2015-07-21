﻿using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace JSI
{
    /*************************************************************************
     * JSIHeadsUpDisplay provides an alternative to the Primary Flight Display
     * for use in aircraft.  Instead of a spherical nav ball, pitch and roll
     * are displayed with a "ladder" (texture).  Strips also provide heading
     * information.
     ************************************************************************/
    class JSIHeadsUpDisplay : InternalModule
    {
        [KSPField]
        public int drawingLayer = 17;

        [KSPField]
        public string backgroundColor = "0,0,0,0";
        private Color32 backgroundColorValue;

        // Static overlay
        [KSPField]
        public string staticOverlay = string.Empty;

        // Ladder
        [KSPField]
        public Vector2 horizonSize = new Vector2(64.0f, 32.0f);
        [KSPField]
        public string horizonTexture = string.Empty;
        [KSPField]
        public bool use360horizon = true;
        [KSPField] // Number of texels of the horizon texture to draw (width).
        public Vector2 horizonTextureSize = new Vector2(1f, 1f);

        [KSPField]
        public string headingBar = string.Empty;
        [KSPField] // x,y, width, height in pixels
        public Vector4 headingBarPosition = new Vector4(0f, 0f, 0f, 0f);
        [KSPField]
        public float headingBarWidth = 0.0f;
        private float headingBarTextureWidth;

        [KSPField]
        public bool showHeadingBarPrograde = true;
        [KSPField]
        public bool showLadderPrograde = true;
        [KSPField]
        public string progradeColor = string.Empty;
        private Color progradeColorValue = new Color(0.84f, 0.98f, 0);
        [KSPField]
        public float iconPixelSize = 64f;

        // Vertical bars
        [KSPField]
        public string verticalBar = string.Empty;
        private List<VerticalBar> verticalBars = new List<VerticalBar>();

        private GameObject cameraBody;
        private Camera hudCamera;

        private RasterPropMonitorComputer comp;

        private GameObject ladderMesh;
        private GameObject progradeLadderIcon;
        private GameObject overlayMesh;
        private GameObject headingMesh;
        private GameObject progradeHeadingIcon;
        private float progradeHeadingIconOrigin;

        private float lastRoll = 0.0f;

        private bool startupComplete;
        private bool firstRenderComplete;


        /// <summary>
        /// Initialize the renderable game objects for the HUD.
        /// </summary>
        /// <param name="screenWidth"></param>
        /// <param name="screenHeight"></param>
        void InitializeRenderables(float screenWidth, float screenHeight)
        {
            Shader displayShader = JUtil.LoadInternalShader("RPM-DisplayShader");

            if (!string.IsNullOrEmpty(staticOverlay))
            {
                Material overlayMaterial = new Material(displayShader);
                overlayMaterial.color = Color.white;
                Texture overlayTexture = GameDatabase.Instance.GetTexture(staticOverlay.EnforceSlashes(), false);
                overlayMaterial.mainTexture = overlayTexture;

                overlayMesh = JUtil.CreateSimplePlane("JSIHeadsUpDisplayOverlay" + hudCamera.GetInstanceID(), screenWidth * 0.5f, drawingLayer);
                overlayMesh.transform.position = new Vector3(0, 0, 1.0f);
                overlayMesh.renderer.material = overlayMaterial;
                overlayMesh.transform.parent = cameraBody.transform;

                JUtil.ShowHide(false, overlayMesh);
            }

            if (!string.IsNullOrEmpty(horizonTexture))
            {
                Shader ladderShader = JUtil.LoadInternalShader("RPM-CroppedDisplayShader");
                Material ladderMaterial = new Material(ladderShader);

                // _CropBound is in device normalized coordinates (-1 - +1)
                Vector4 cropBound = new Vector4(-horizonSize.x / screenWidth, -horizonSize.y / screenHeight, horizonSize.x / screenWidth, horizonSize.y / screenHeight);
                ladderMaterial.SetVector("_CropBound", cropBound);
                ladderMaterial.color = Color.white;
                ladderMaterial.mainTexture = GameDatabase.Instance.GetTexture(horizonTexture.EnforceSlashes(), false);
                if (ladderMaterial.mainTexture != null)
                {
                    horizonTextureSize.x = 0.5f * (horizonTextureSize.x / ladderMaterial.mainTexture.width);
                    horizonTextureSize.y = 0.5f * (horizonTextureSize.y / ladderMaterial.mainTexture.height);

                    ladderMaterial.mainTexture.wrapMode = TextureWrapMode.Clamp;

                    ladderMesh = JUtil.CreateSimplePlane("JSIHeadsUpDisplayLadder" + hudCamera.GetInstanceID(), new Vector2(horizonSize.x * 0.5f, horizonSize.y * 0.5f), new Rect(0.0f, 0.0f, 1.0f, 1.0f), drawingLayer);
                    ladderMesh.transform.position = new Vector3(0, 0, 1.4f);
                    ladderMesh.renderer.material = ladderMaterial;
                    ladderMesh.transform.parent = cameraBody.transform;

                    JUtil.ShowHide(false, ladderMesh);

                    if (progradeColorValue.a > 0.0f && showLadderPrograde)
                    {
                        Material progradeIconMaterial = new Material(displayShader);
                        progradeIconMaterial.color = Color.white;
                        progradeIconMaterial.mainTexture = JUtil.GetGizmoTexture();
                        progradeIconMaterial.SetVector("_Color", progradeColorValue);

                        progradeLadderIcon = JUtil.CreateSimplePlane("JSIHeadsUpDisplayLadderProgradeIcon" + hudCamera.GetInstanceID(), new Vector2(iconPixelSize * 0.5f, iconPixelSize * 0.5f), GizmoIcons.GetIconLocation(GizmoIcons.IconType.PROGRADE), drawingLayer);
                        progradeLadderIcon.transform.position = new Vector3(0.0f, 0.0f, 1.35f);
                        progradeLadderIcon.renderer.material = progradeIconMaterial;
                        progradeLadderIcon.transform.parent = cameraBody.transform;
                    }
                }
            }

            if (!string.IsNullOrEmpty(headingBar))
            {
                Material headingMaterial = new Material(displayShader);
                headingMaterial.color = Color.white;
                headingMaterial.mainTexture = GameDatabase.Instance.GetTexture(headingBar.EnforceSlashes(), false);
                if (headingMaterial.mainTexture != null)
                {
                    headingBarTextureWidth = 0.5f * (headingBarWidth / (float)headingMaterial.mainTexture.width);

                    headingMaterial.mainTexture.wrapMode = TextureWrapMode.Repeat;

                    headingMesh = JUtil.CreateSimplePlane("JSIHeadsUpDisplayHeading" + hudCamera.GetInstanceID(), new Vector2(headingBarPosition.z * 0.5f, headingBarPosition.w * 0.5f), new Rect(0.0f, 0.0f, 1.0f, 1.0f), drawingLayer);
                    headingMesh.transform.position = new Vector3(headingBarPosition.x + 0.5f * (headingBarPosition.z - screenWidth), 0.5f * (screenHeight - headingBarPosition.w) - headingBarPosition.y, 1.4f);
                    headingMesh.renderer.material = headingMaterial;
                    headingMesh.transform.parent = cameraBody.transform;

                    JUtil.ShowHide(false, headingMesh);

                    if (progradeColorValue.a > 0.0f && showHeadingBarPrograde)
                    {
                        Material progradeIconMaterial = new Material(displayShader);
                        progradeIconMaterial.color = Color.white;
                        progradeIconMaterial.mainTexture = JUtil.GetGizmoTexture();
                        progradeIconMaterial.SetVector("_Color", progradeColorValue);

                        progradeHeadingIconOrigin = headingBarPosition.x + 0.5f * (headingBarPosition.z - screenWidth);

                        progradeHeadingIcon = JUtil.CreateSimplePlane("JSIHeadsUpDisplayHeadingProgradeIcon" + hudCamera.GetInstanceID(), new Vector2(iconPixelSize * 0.5f, iconPixelSize * 0.5f), GizmoIcons.GetIconLocation(GizmoIcons.IconType.PROGRADE), drawingLayer);
                        progradeHeadingIcon.transform.position = new Vector3(progradeHeadingIconOrigin, 0.5f * (screenHeight - headingBarPosition.w) - headingBarPosition.y, 1.35f);
                        progradeHeadingIcon.renderer.material = progradeIconMaterial;
                        progradeHeadingIcon.transform.parent = headingMesh.transform;
                    }
                }
            }

            if (!string.IsNullOrEmpty(verticalBar))
            {
                ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("JSIHUD_VERTICAL_BAR");
                string[] vBars = verticalBar.Split(';');
                for (int i = 0; i < vBars.Length; ++i)
                {
                    for (int j = 0; j < nodes.Length; ++j)
                    {
                        if (nodes[j].HasValue("name") && vBars[i].Trim() == nodes[j].GetValue("name"))
                        {
                            try
                            {
                                VerticalBar vb = new VerticalBar(nodes[j], screenWidth, screenHeight, drawingLayer, displayShader, cameraBody);
                                verticalBars.Add(vb);
                            }
                            catch (Exception e)
                            {
                                JUtil.LogErrorMessage(this, "Error parsing JSIHUD_VERTICAL_BAR: {0}", e);
                            }
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Update the ladder's texture UVs so it's drawn correctly
        /// </summary>
        private void UpdateLadder()
        {
            float pitch = 90.0f - Vector3.Angle(comp.Forward, comp.Up);

            float ladderMidpointCoord;
            if (use360horizon)
            {
                // Straight up is texture coord 0.75;
                // Straight down is TC 0.25;
                ladderMidpointCoord = JUtil.DualLerp(0.25f, 0.75f, -90f, 90f, pitch);
            }
            else
            {
                // Straight up is texture coord 1.0;
                // Straight down is TC 0.0;
                ladderMidpointCoord = JUtil.DualLerp(0.0f, 1.0f, -90f, 90f, pitch);
            }

            // MOARdV TODO: These can be done without manually editing the 
            // mesh filter.  I need to look up the game object texture stuff.
            MeshFilter meshFilter = ladderMesh.GetComponent<MeshFilter>();

            meshFilter.mesh.uv = new[] 
            {
                new Vector2(0.5f - horizonTextureSize.x, ladderMidpointCoord - horizonTextureSize.y),
                new Vector2(0.5f + horizonTextureSize.x, ladderMidpointCoord - horizonTextureSize.y),
                new Vector2(0.5f - horizonTextureSize.x, ladderMidpointCoord + horizonTextureSize.y),
                new Vector2(0.5f + horizonTextureSize.x, ladderMidpointCoord + horizonTextureSize.y)
            };

            Quaternion rotationVesselSurface = comp.RotationVesselSurface;
            float roll = rotationVesselSurface.eulerAngles.z;

            ladderMesh.transform.Rotate(new Vector3(0.0f, 0.0f, 1.0f), lastRoll - roll);

            lastRoll = roll;

            if (progradeLadderIcon != null)
            {
                Vector3 velocityVesselSurfaceUnit = comp.VelocityVesselSurface.normalized;
                Vector3 tmpVec = comp.Up * Vector3.Dot(comp.Up, velocityVesselSurfaceUnit) + comp.SurfaceForward * Vector3.Dot(comp.SurfaceForward, velocityVesselSurfaceUnit);
                float AoA = Vector3.Dot(tmpVec.normalized, comp.Up);
                AoA = Mathf.Rad2Deg * Mathf.Asin(AoA);
                if (float.IsNaN(AoA))
                {
                    AoA = 0.0f;
                }

                float AoATC;
                if (use360horizon)
                {
                    // Straight up is texture coord 0.75;
                    // Straight down is TC 0.25;
                    AoATC = JUtil.DualLerp(0.25f, 0.75f, -90f, 90f, AoA);
                }
                else
                {
                    // Straight up is texture coord 1.0;
                    // Straight down is TC 0.0;
                    AoATC = JUtil.DualLerp(0.0f, 1.0f, -90f, 90f, AoA);
                }

                float Ypos = JUtil.DualLerp(
                                 -horizonSize.y * 0.5f, horizonSize.y * 0.5f,
                                 ladderMidpointCoord - horizonTextureSize.y, ladderMidpointCoord + horizonTextureSize.y,
                                 AoATC);

                Vector3 position = progradeLadderIcon.transform.position;
                position.x = Ypos * Mathf.Sin(roll * Mathf.Deg2Rad);
                position.y = Ypos * Mathf.Cos(roll * Mathf.Deg2Rad);
                progradeLadderIcon.transform.position = position;

                JUtil.ShowHide(true, progradeLadderIcon);
            }
        }

        /// <summary>
        /// Update the compass / heading bar
        /// </summary>
        private void UpdateHeading()
        {
            float heading = comp.RotationVesselSurface.eulerAngles.y / 360.0f;

            MeshFilter meshFilter = headingMesh.GetComponent<MeshFilter>();

            meshFilter.mesh.uv = new[] 
            {
                new Vector2(heading - headingBarTextureWidth, 0.0f),
                new Vector2(heading + headingBarTextureWidth, 0.0f),
                new Vector2(heading - headingBarTextureWidth, 1.0f),
                new Vector2(heading + headingBarTextureWidth, 1.0f)
            };

            if (progradeHeadingIcon != null)
            {
                Vector3 velocityVesselSurfaceUnit = comp.VelocityVesselSurface.normalized;
                float slipAngle = velocityVesselSurfaceUnit.AngleInPlane(comp.Up, comp.Forward);
                float slipTC = JUtil.DualLerp(0f, 1f, 0f, 360f, comp.RotationVesselSurface.eulerAngles.y + slipAngle);
                float slipIconX = JUtil.DualLerp(progradeHeadingIconOrigin - 0.5f * headingBarPosition.z, progradeHeadingIconOrigin + 0.5f * headingBarPosition.z, heading - headingBarTextureWidth, heading + headingBarTextureWidth, slipTC);

                Vector3 position = progradeHeadingIcon.transform.position;
                position.x = slipIconX;
                progradeHeadingIcon.transform.position = position;

                JUtil.ShowHide(true, progradeHeadingIcon);
            }
        }

        public bool RenderHUD(RenderTexture screen, float cameraAspect)
        {
            if (screen == null || !startupComplete || HighLogic.LoadedSceneIsEditor)
            {
                return false;
            }

            if (!firstRenderComplete)
            {
                firstRenderComplete = true;
                hudCamera.orthographicSize = (float)(screen.height) * 0.5f;
                hudCamera.aspect = (float)screen.width / (float)screen.height;
                InitializeRenderables((float)screen.width, (float)screen.height);
            }

            for (int i = 0; i < verticalBars.Count; ++i)
            {
                verticalBars[i].Update(comp);
            }

            GL.Clear(true, true, backgroundColorValue);

            hudCamera.targetTexture = screen;

            // MOARdV TODO: I don't think this does anything...
            GL.Color(Color.white);

            if (headingMesh != null)
            {
                UpdateHeading();
                JUtil.ShowHide(true, headingMesh);
            }

            if (ladderMesh != null)
            {
                // Viewport doesn't work with this, AFAICT.
                // Anyway, these numbers aren't right for the redesigned HUD.
                //JUtil.LogMessage(this, "screen is {0} x {1}, horizon size is {2} x {3}, making a rectangle at {4} x {5} with size of {6} x {7}",
                //    screen.width,screen.height,
                //    horizonSize.x, horizonSize.y,
                //    (screen.width - horizonSize.x) * 0.5f, (screen.height - horizonSize.y) * 0.5f,
                //    horizonSize.x, horizonSize.y);
                //GL.Viewport(new Rect((screen.width - horizonSize.x) * 0.5f, (screen.height - horizonSize.y) * 0.5f, horizonSize.x, horizonSize.y));
                // Fix up UVs, apply rotation.
                UpdateLadder();
                JUtil.ShowHide(true, ladderMesh);
                //hudCamera.Render();
                //JUtil.ShowHide(false, ladderMesh);
                //GL.Viewport(new Rect(0, 0, screen.width, screen.height));
            }

            if (overlayMesh != null)
            {
                JUtil.ShowHide(true, overlayMesh);
            }

            hudCamera.Render();

            JUtil.ShowHide(false, overlayMesh, ladderMesh, headingMesh, progradeLadderIcon, progradeHeadingIcon);
            for (int i = 0; i < verticalBars.Count; ++i)
            {
                JUtil.ShowHide(false, verticalBars[i].barObject);
            }

            return true;
        }

        public void Start()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                return;
            }
            try
            {
                backgroundColorValue = ConfigNode.ParseColor32(backgroundColor);

                cameraBody = new GameObject();
                cameraBody.name = "RPMPFD" + cameraBody.GetInstanceID();
                cameraBody.layer = drawingLayer;
                hudCamera = cameraBody.AddComponent<Camera>();
                hudCamera.enabled = false;
                hudCamera.orthographic = true;
                hudCamera.eventMask = 0;
                hudCamera.farClipPlane = 3f;
                hudCamera.orthographicSize = 1.0f;
                hudCamera.cullingMask = 1 << drawingLayer;
                // does this actually work?
                hudCamera.backgroundColor = backgroundColorValue;
                hudCamera.clearFlags = CameraClearFlags.Depth | CameraClearFlags.Color;
                hudCamera.transparencySortMode = TransparencySortMode.Orthographic;
                hudCamera.transform.position = Vector3.zero;
                hudCamera.transform.LookAt(new Vector3(0.0f, 0.0f, 1.5f), Vector3.up);

                if (!string.IsNullOrEmpty(progradeColor))
                {
                    progradeColorValue = ConfigNode.ParseColor32(progradeColor);
                }

                // use the RPM comp's centralized database so we're not 
                // repeatedly doing computation.
                comp = RasterPropMonitorComputer.Instantiate(this.part);
            }
            catch (Exception e)
            {
                JUtil.LogErrorMessage(this, "Start() failed with an exception: {0}", e);
                JUtil.AnnoyUser(this);
                throw;
            }

            startupComplete = true;
        }

        public void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                // Nothing configured, nothing to destroy.
                return;
            }

            JUtil.DisposeOfGameObjects(new GameObject[] { ladderMesh, progradeLadderIcon, overlayMesh, headingMesh, progradeHeadingIcon });
            for (int i = 0; i < verticalBars.Count; ++i)
            {
                JUtil.DisposeOfGameObjects(new GameObject[] { verticalBars[i].barObject });
            }
        }
    }

    class VerticalBar
    {
        private VariableOrNumber variable;
        private Vector2 scale;
        private Vector2 textureLimit;
        public readonly GameObject barObject;
        private float textureSize;
        private bool useLog10;

        internal VerticalBar(ConfigNode node, float screenWidth, float screenHeight, int drawingLayer, Shader displayShader, GameObject cameraBody)
        {
            JUtil.LogMessage(this, "Configuring for {0}", node.GetValue("name"));
            if (!node.HasValue("variableName"))
            {
                throw new Exception("VerticalBar " + node.GetValue("name") + " missing variableName");
            }
            variable = new VariableOrNumber(node.GetValue("variableName"), this);

            if (!node.HasValue("texture"))
            {
                throw new Exception("VerticalBar " + node.GetValue("name") + " missing texture");
            }

            Texture2D tex = GameDatabase.Instance.GetTexture(node.GetValue("texture"), false);
            if (tex == null)
            {
                throw new Exception("VerticalBar " + node.GetValue("name") + " texture " + node.GetValue("texture") + " can't be loaded.");
            }
            tex.wrapMode = TextureWrapMode.Clamp;

            if (node.HasValue("useLog10") && bool.TryParse(node.GetValue("useLog10"), out useLog10) == false)
            {
                // I think this is redundant
                useLog10 = false;
            }

            if (!node.HasValue("scale"))
            {
                throw new Exception("VerticalBar " + node.GetValue("name") + " missing scale");
            }

            scale = ConfigNode.ParseVector2(node.GetValue("scale"));
            if (useLog10)
            {
                scale.x = JUtil.PseudoLog10(scale.x);
                scale.y = JUtil.PseudoLog10(scale.y);
            }

            if (!node.HasValue("textureSize"))
            {
                throw new Exception("VerticalBar " + node.GetValue("name") + " missing textureSize");
            }

            if (!float.TryParse(node.GetValue("textureSize"), out textureSize))
            {
                throw new Exception("VerticalBar " + node.GetValue("name") + " failed parsing textureSize");
            }

            textureSize = 0.5f * textureSize / (float)tex.height;

            if (!node.HasValue("textureLimit"))
            {
                throw new Exception("VerticalBar " + node.GetValue("name") + " missing textureLimit");
            }

            textureLimit = ConfigNode.ParseVector2(node.GetValue("textureLimit"));
            textureLimit.x = 1.0f - textureLimit.x / (float)tex.height;
            textureLimit.y = 1.0f - textureLimit.y / (float)tex.height;

            if (!node.HasValue("position"))
            {
                throw new Exception("VerticalBar " + node.GetValue("name") + " missing position");
            }

            Vector4 position = ConfigNode.ParseVector4(node.GetValue("position"));

            barObject = JUtil.CreateSimplePlane("VerticalBar" + node.GetValue("name"), new Vector2(0.5f * position.z, 0.5f * position.w), new Rect(0.0f, 0.0f, 1.0f, 1.0f), drawingLayer);

            Material barMaterial = new Material(displayShader);
            barMaterial.color = Color.white;
            barMaterial.mainTexture = tex;

            // Position in camera space has (0, 0) in the center, so we need to
            // translate everything appropriately.  Y is odd since the coordinates
            // supplied are Left-Handed (0Y on top, growing down), not RH.
            barObject.transform.position = new Vector3(position.x + 0.5f * (position.z - screenWidth), 0.5f * (screenHeight - position.w) - position.y, 1.4f);
            barObject.renderer.material = barMaterial;
            barObject.transform.parent = cameraBody.transform;

            JUtil.ShowHide(true, barObject);
        }

        internal void Update(RasterPropMonitorComputer comp)
        {
            float value;
            if (variable.Get(out value, comp))
            {
                if (useLog10)
                {
                    value = JUtil.PseudoLog10(value);
                }
                float yOffset = JUtil.DualLerp(textureLimit, scale, value);

                MeshFilter meshFilter = barObject.GetComponent<MeshFilter>();

                meshFilter.mesh.uv = new[] 
                {
                    new Vector2(0.0f, yOffset - textureSize),
                    new Vector2(1.0f, yOffset - textureSize),
                    new Vector2(0.0f, yOffset + textureSize),
                    new Vector2(1.0f, yOffset + textureSize)
                };

                JUtil.ShowHide(true, barObject);
            }
        }
    }
}
