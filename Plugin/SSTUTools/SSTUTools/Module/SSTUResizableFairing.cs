﻿using System;
using UnityEngine;

namespace SSTUTools
{
    class SSTUResizableFairing : PartModule, IPartMassModifier, IPartCostModifier, IRecolorable
    {

        /// <summary>
        /// Minimum diameter of the model that can be selected by user
        /// </summary>
        [KSPField]
        public float minDiameter = 0.625f;

        /// <summary>
        /// Maximum diameter of the model that can be selected by user
        /// </summary>
        [KSPField]
        public float maxDiameter = 10;
        
        [KSPField]
        public float diameterIncrement = 0.625f;
        
        [KSPField]
        public float topNodePosition = 1f;

        [KSPField]
        public float bottomNodePosition = -0.25f;

        /// <summary>
        /// Default diameter of the model
        /// </summary>
        [KSPField]
        public float modelDiameter = 5f;

        /// <summary>
        /// Default/config diameter of the fairing, in case it differs from model diameter; model scale is applied to this to maintain correct scaling
        /// </summary>
        [KSPField]
        public float fairingDiameter = 5f;
        
        /// <summary>
        /// The max fairing diameter at the default base diameter; this setting gets scaled according to the fairing base size
        /// </summary>
        [KSPField]
        public float defaultMaxDiameter = 5f;

        /// <summary>
        /// root transform of the model, for scaling
        /// </summary>
        [KSPField]
        public String modelName = "SSTU/Assets/SC-GEN-FR";

        /// <summary>
        /// Persistent scale value, whatever value is here/in the config will be the 'start diameter' for parts in the editor/etc
        /// </summary>
        [KSPField(isPersistant = true, guiName ="Diameter", guiActiveEditor = true),
         UI_FloatEdit(sigFigs = 3, suppressEditorShipModified = true)]
        public float currentDiameter = 1.25f;

        [KSPField(isPersistant = true, guiName = "Texture", guiActiveEditor = true),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public String currentTextureSet = "Fairings-White";

        [KSPField(isPersistant = true)]
        public Vector4 customColor1 = new Vector4(1, 1, 1, 1);

        [KSPField(isPersistant = true)]
        public Vector4 customColor2 = new Vector4(1, 1, 1, 1);

        [KSPField(isPersistant = true)]
        public Vector4 customColor3 = new Vector4(1, 1, 1, 1);

        [KSPField(isPersistant = true)]
        public bool initializedColors = false;

        [Persistent]
        public string configNodeData = string.Empty;

        private float prevDiameter;
        private ModuleProceduralFairing mpf = null;

        public void onTextureUpdated(BaseField field, object obj)
        {
            this.actionWithSymmetry(m => 
            {
                m.currentTextureSet = currentTextureSet;
                m.updateTextureSet(!SSTUGameSettings.persistRecolor());
            });
        }

        public void onDiameterUpdated(BaseField field, object obj)
        {
            if (prevDiameter != currentDiameter)
            {
                prevDiameter = currentDiameter;
                onUserSizeChange(currentDiameter, true);
            }
        }

        public void onUserSizeChange(float newDiameter, bool updateSymmetry)
        {
            if (newDiameter > maxDiameter) { newDiameter = maxDiameter; }
            if (newDiameter < minDiameter) { newDiameter = minDiameter; }
            currentDiameter = newDiameter;
            updateModelScale();
            mpf.DeleteFairing();
            updateNodePositions(true);
            updateEditorFields();
            if (updateSymmetry)
            {
                foreach (Part p in part.symmetryCounterparts) { p.GetComponent<SSTUResizableFairing>().onUserSizeChange(currentDiameter, false); }
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            mpf = part.GetComponent<ModuleProceduralFairing>();
            ConfigNode node = SSTUConfigNodeUtils.parseConfigNode(configNodeData);

            ConfigNode[] textureNodes = node.GetNodes("TEXTURESET");
            string[] names = SSTUTextureUtils.getTextureSetNames(textureNodes);
            string[] titles = SSTUTextureUtils.getTextureSetTitles(textureNodes);
            TextureSet t = SSTUTextureUtils.getTextureSet(currentTextureSet);
            if (t == null)
            {
                currentTextureSet = names[0];
                t = SSTUTextureUtils.getTextureSet(currentTextureSet);
                initializedColors = false;
            }
            if (!initializedColors)
            {
                initializedColors = true;
                Color[] cs = t.maskColors;
                customColor1 = cs[0];
                customColor2 = cs[1];
                customColor3 = cs[2];
            }
            this.updateUIChooseOptionControl(nameof(currentTextureSet), names, titles, true, currentTextureSet);
            Fields[nameof(currentTextureSet)].guiActiveEditor = names.Length > 1;

            updateModelScale();
            updateTextureSet(false);
            updateNodePositions(false);
            this.updateUIFloatEditControl("currentDiameter", minDiameter, maxDiameter, diameterIncrement*2f, diameterIncrement, diameterIncrement*0.05f, true, currentDiameter);
            Fields["currentDiameter"].uiControlEditor.onFieldChanged = onDiameterUpdated;
            Fields["currentTextureSet"].uiControlEditor.onFieldChanged = onTextureUpdated;
            updateEditorFields();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (string.IsNullOrEmpty(configNodeData)) { configNodeData = node.ToString(); }
            mpf = part.GetComponent<ModuleProceduralFairing>();
            updateModelScale();//for prefab part...
            updateEditorFields();
        }

        public void Start()
        {
            mpf = part.GetComponent<ModuleProceduralFairing>();
            updateModelScale();//make sure to update the mpf after it is initialized
            updateTextureSet(false);
        }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
        {
            float scale = currentDiameter / modelDiameter;
            return -defaultMass + defaultMass * Mathf.Pow(scale, 3f);
        }

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit)
        {
            float scale = currentDiameter / modelDiameter;
            return -defaultCost + defaultCost * Mathf.Pow(scale, 3f);
        }
        public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }
        public ModifierChangeWhen GetModuleCostChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }

        public string[] getSectionNames()
        {
            return new string[] { "Decoupler" };
        }

        public Color[] getSectionColors(string name)
        {
            return new Color[] { customColor1, customColor2, customColor3 };
        }

        public void setSectionColors(string name, Color[] colors)
        {
            customColor1 = colors[0];
            customColor2 = colors[1];
            customColor3 = colors[2];
            updateTextureSet(false);
        }

        private void updateEditorFields()
        {
            prevDiameter = currentDiameter;
        }        

        private void updateModelScale()
        {
            float scale = currentDiameter / modelDiameter;
            Transform tr = part.transform.FindModel(modelName);
            if (tr != null)
            {
                tr.localScale = new Vector3(scale, scale, scale);
            }
            if (mpf != null)
            {
                mpf.baseRadius = scale * fairingDiameter * 0.5f;
                mpf.maxRadius = scale * defaultMaxDiameter * 0.5f;
            }
        }

        private void updateNodePositions(bool userInput)
        {
            AttachNode topNode = part.FindAttachNode("top");
            AttachNode bottomNode = part.FindAttachNode("bottom");
            float scale = currentDiameter / modelDiameter;
            float topY = topNodePosition * scale;
            float bottomY = bottomNodePosition * scale;
            Vector3 pos = new Vector3(0, topY, 0);
            SSTUAttachNodeUtils.updateAttachNodePosition(part, topNode, pos, topNode.orientation, userInput);
            pos = new Vector3(0, bottomY, 0);
            SSTUAttachNodeUtils.updateAttachNodePosition(part, bottomNode, pos, bottomNode.orientation, userInput);
        }

        private void updateTextureSet(bool useDefaults)
        {
            if (mpf == null) { return; }
            TextureSet s = SSTUTextureUtils.getTextureSet(currentTextureSet);
            Color[] colors = useDefaults ? s.maskColors : getSectionColors(string.Empty);
            Material fm = mpf.FairingMaterial;
            if (fm != null)
            {
                s.textureData[0].enable(fm, colors);
            }
            foreach (ProceduralFairings.FairingPanel fp in mpf.Panels)
            {
                s.enable(fp.go, colors);
            }
            if (useDefaults)
            {
                customColor1 = colors[0];
                customColor2 = colors[1];
                customColor3 = colors[2];
            }
            SSTUModInterop.onPartTextureUpdated(part);
        }

    }
}
