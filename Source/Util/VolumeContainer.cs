﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SSTUTools
{ 
    /// <summary>
    /// Container definition type that determines what resources may be loaded into a given part, what container types are usable, and what fuel types are available; 
    /// multiple containers may be used on a part to segregate the available storage types;
    /// </summary>
    public class ContainerDefinition
    {
        //Config fields - loaded from config node; arrays are loaded as key=value pairs
        public readonly string name = "Main";// config specified tank name; used to display the name and for ease of MM node patching
        public readonly string[] availableResources;// user config specified resources; resource=XXX
        public readonly string[] resourceSets;// user config specified resource sets; resourceSet=XXX
        public readonly string[] tankModifierNames;// user config specified tank mods; modifer=XXX
        public readonly float tankageVolume;// percent of volume lost to tankage
        public readonly float tankageMass;// percent of resource mass or volume to compute as dry mass
        public readonly float costPerDryTon;// default cost per dry ton for this tank; modified by the tank modifier
        public readonly float massPerEmptyCubicMeter;// how much does this tank weigh per m^3 when it is empty or for unused volume (not yet supported)
        public readonly string defaultFuelPreset;// user config specified default fuel preset
        public readonly string defaultResources;//CSV resource,ratio,resource,ratio...
        public readonly string defaultModifier;// the default tank modifier; set to first modifier if it is blank or invalid

        //runtime accessible data
        public readonly string[] applicableResources;
        public readonly ContainerFuelPreset[] fuelPresets;
        public readonly ContainerModifier[] modifiers;

        //private vars
        private SubContainerDefinition[] subContainerData;
        private Dictionary<string, SubContainerDefinition> subContainersByName = new Dictionary<string, SubContainerDefinition>();

        private ContainerModifier cachedModifier;
        private float percentOfTankVolume;//loaded from config, but private for better encapsulation
        private string currentFuelPreset;
        private float currentRawVolume;
        private float currentUsableVolume;
        private string currentModifierName;
        private float currentResourceMass;
        private float currentResourceCost;
        private float currentContainerMass;
        private float currentContainerCost;
        private int currentTotalUnitRatio;
        private float currentTotalVolumeRatio;
        private bool resourcesDirty = false;

        public ContainerDefinition(ConfigNode node, float tankTotalVolume)
        {
            name = node.GetStringValue("name", name);
            availableResources = node.GetStringValues("resource");
            resourceSets = node.GetStringValues("resourceSet");
            tankModifierNames = node.GetStringValues("modifier");
            setContainerPercent(node.GetFloatValue("percent", 1));
            tankageVolume = node.GetFloatValue("tankageVolume");
            tankageMass = node.GetFloatValue("tankageMass");
            costPerDryTon = node.GetFloatValue("dryCost", 700f);
            massPerEmptyCubicMeter = node.GetFloatValue("emptyMass", 0.05f);
            defaultFuelPreset = node.GetStringValue("defaultFuelPreset");
            defaultResources = node.GetStringValue("defaultResources");
            defaultModifier = node.GetStringValue("defaultModifier", "standard");

            if (availableResources.Length == 0 && resourceSets.Length == 0) { resourceSets = new string[] { "generic" }; }//validate that there is some sort of resource reference; generic is a special type for all pumpable resources
            if (tankModifierNames == null || tankModifierNames.Length == 0) { tankModifierNames = VolumeContainerLoader.getAllModifierNames(); }//validate that there is at least one modifier type            
            
            //load available container modifiers
            modifiers = VolumeContainerLoader.getModifiersByName(tankModifierNames);

            //setup applicable resources
            List<string> resourceNames = new List<string>();
            resourceNames.AddRange(availableResources);
            int len = resourceSets.Length;
            int resLen;
            ContainerResourceSet set;
            for (int i = 0; i < len; i++)
            {
                set = VolumeContainerLoader.getResourceSet(resourceSets[i]);
                if (set == null) { continue; }
                resLen = set.availableResources.Length;
                for (int k = 0; k < resLen; k++)
                {
                    resourceNames.AddUnique(set.availableResources[k]);
                }
            }
            resourceNames.Sort();//basic alpha sort...
            applicableResources = resourceNames.ToArray();

            if (string.IsNullOrEmpty(defaultFuelPreset) && string.IsNullOrEmpty(defaultResources) && applicableResources.Length > 0) { defaultResources = applicableResources[0]+",1"; }

            //setup volume data
            len = applicableResources.Length;
            subContainerData = new SubContainerDefinition[len];
            for (int i = 0; i < len; i++)
            {
                subContainerData[i] = new SubContainerDefinition(this, applicableResources[i]);
                subContainersByName.Add(subContainerData[i].name, subContainerData[i]);
            }

            //setup preset data
            List<ContainerFuelPreset> usablePresets = new List<ContainerFuelPreset>();
            ContainerFuelPreset[] presets = VolumeContainerLoader.getPresets();
            len = presets.Length;
            for (int i = 0; i < len; i++)
            {
                if (presets[i].applicable(applicableResources)) { usablePresets.Add(presets[i]); }
            }
            fuelPresets = usablePresets.ToArray();
            currentModifierName = defaultModifier;
            currentRawVolume = tankTotalVolume * percentOfTankVolume;
            internalInitializeDefaults();
        }

        public void loadPersistenData(string data)
        {
            string[] vals = data.Split(',');
            currentModifierName = vals[0];
            currentFuelPreset = vals[1];
            setContainerPercent(float.Parse(vals[2]));
            int len = subContainerData.Length;
            int len2 = vals.Length;
            for (int i = 0, k = 3; i < len && k < len2; i++, k++)
            {
                subContainerData[i].setRatio(int.Parse(vals[k]));
            }
            internalUpdateTotalRatio();
            internalUpdateVolumeUnits();
            internalUpdateMassAndCost();
        }

        public string getPersistentData()
        {
            string data = currentModifierName + "," + currentFuelPreset + ","+percentOfTankVolume;
            int len = subContainerData.Length;
            for (int i = 0; i < len; i++)
            {
                data = data + "," + subContainerData[i].unitRatio;
            }
            return data;
        }

        public int totalUnitRatio { get { return currentTotalUnitRatio; } }

        public float totalVolumeRatio { get { return currentTotalVolumeRatio; } }

        public float containerMass{ get { return currentContainerMass; } }
        
        public float containerCost{ get { return currentContainerCost; } }

        public float containerPercent { get { return percentOfTankVolume; } }

        public float resourceMass { get { return currentResourceMass; } }

        public float resourceCost { get { return currentResourceCost; } }

        public int getResourceUnitRatio(string name) { return internalGetVolumeData(name).unitRatio; }

        public float getResourceVolumeRatio(string name) { return internalGetVolumeData(name).volumeRatio; }

        public float getResourceVolume(string name) { return internalGetVolumeData(name).resourceVolume; }

        public float getResourceUnits(string name) { return internalGetVolumeData(name).resourceUnits; }

        public float rawVolume { get { return currentRawVolume; } }

        public float usableVolume { get { return currentUsableVolume; } }

        public string fuelPreset { get { return currentFuelPreset; } }

        public int Length { get { return subContainerData.Length; } }

        public bool isDirty { get { return resourcesDirty; } }

        public void clearDirty() { resourcesDirty = false; }

        public ContainerModifier currentModifier
        {
            get
            {
                if (cachedModifier == null || cachedModifier.name != currentModifierName) { cachedModifier = internalGetModifier(currentModifierName); }
                return cachedModifier;
            }
        }
        
        //TODO can this just return applicableResources?
        public string[] getResourceNames()
        {
            int len = subContainerData.Length;
            string[] names = new string[len];
            for (int i = 0; i < len; i++)
            {
                names[i] = subContainerData[i].name;
            }
            return names;
        }

        public void getResources(SSTUResourceList list)
        {
            int len = subContainerData.Length;
            for (int i = 0; i < len; i++)
            {
                list.addResource(subContainerData[i].name, subContainerData[i].resourceUnits);
            }
        }

        public void setModifier(ContainerModifier mod)
        {
            currentModifierName = mod.name;
            internalUpdateVolumeUnits();
            internalUpdateMassAndCost();
            resourcesDirty = true;
        }

        public void setResourceRatio(string name, int newRatio)
        {
            internalGetVolumeData(name).setRatio(newRatio);
            internalUpdateTotalRatio();
            internalUpdateVolumeUnits();
            internalUpdateMassAndCost();
            resourcesDirty = true;
        }

        public void setContainerVolume(float partRawVolume)
        {
            currentRawVolume = partRawVolume * percentOfTankVolume;
            internalUpdateVolumeUnits();
            internalUpdateMassAndCost();
            resourcesDirty = true;
        }

        /// <summary>
        /// UNSAFE - Must manually update container parameters for all containers after calling this method on any container.
        /// </summary>
        /// <param name="newPercent"></param>
        internal void setContainerPercent(float newPercent)
        {
            if (newPercent > 1) { newPercent *= 0.01f; }
            percentOfTankVolume = newPercent;
        }

        /// <summary>
        /// Zeroes any current configuration and sets the tank up for the input fuel preset; must not be null<para/>
        /// Intended to be used by the 'Next Fuel Type' buttons on the base part GUI (or other method to set a valid fuel type)
        /// </summary>
        /// <param name="preset"></param>
        public void setFuelPreset(ContainerFuelPreset preset)
        {
            if (preset == null) { throw new NullReferenceException("Fuel preset was null when calling setFuelPreset"); }
            currentFuelPreset = preset.name;
            internalClearRatios();
            internalAddPresetRatios(preset);
        }

        /// <summary>
        /// -ADDS- the input preset to the current ratios for the tank without adjusting any other resource ratios<para/>
        /// this forces a 'custom' fuel preset type on the tank
        /// </summary>
        /// <param name="preset"></param>
        public void addPresetRatios(ContainerFuelPreset preset)
        {
            internalAddPresetRatios(preset);
            currentFuelPreset = "custom";
        }

        public void subtractPresetRatios(ContainerFuelPreset preset)
        {
            internalAddPresetRatios(preset, true);
            currentFuelPreset = "custom";
        }

        private void internalAddPresetRatios(ContainerFuelPreset preset, bool subtract=false)
        {
            int len = preset.resourceRatios.Length;
            ContainerResourceRatio ratio;
            for (int i = 0; i < len; i++)
            {
                ratio = preset.resourceRatios[i];
                internalGetVolumeData(ratio.resourceName).addRatio(subtract? -ratio.resourceRatio : ratio.resourceRatio);
            }
            internalUpdateTotalRatio();
            internalUpdateVolumeUnits();
            internalUpdateMassAndCost();
            resourcesDirty = true;
        }

        private void internalUpdateMassAndCost()
        {
            currentResourceMass = 0;
            int len = subContainerData.Length;
            float tempMass;
            float zeroMassResourceMass = 0;//if that name doesn't make sense... well...
            for (int i = 0; i < len; i++)
            {
                tempMass = subContainerData[i].resourceMass;
                if (tempMass == 0 && subContainerData[i].resourceVolume > 0)//zero mass resource; fake it for the purposes of
                {                    
                    zeroMassResourceMass += FuelTypes.INSTANCE.getZeroMassResourceMass(subContainerData[i].name) * subContainerData[i].resourceUnits;
                }
                currentResourceMass += tempMass;
            }
                        
            currentContainerMass = 0;
            if (currentModifier.useVolumeForMass)//should most notably be used for structural tank types; any resource-containing tank should use the max resource mass * mass fraction
            {
                tempMass = currentUsableVolume;
                currentContainerMass = tempMass * currentModifier.dryMassModifier * tankageMass;
            }
            else if (totalUnitRatio == 0)//empty tank, use config specified empty tank mass
            {
                currentContainerMass = (currentUsableVolume * 0.001f) * massPerEmptyCubicMeter * currentModifier.dryMassModifier;
            }
            else
            {
                tempMass = currentResourceMass;
                currentContainerMass = tempMass * currentModifier.dryMassModifier * tankageMass;
            }

            currentResourceCost = 0;
            float tempCost;
            for (int i = 0; i < len; i++)
            {
                tempCost = subContainerData[i].resourceCost;
                if (tempCost == 0 && subContainerData[i].resourceUnits > 0)
                {
                    tempCost = FuelTypes.INSTANCE.getZeroCostResourceCost(subContainerData[i].name) * subContainerData[i].resourceUnits;
                }
                currentResourceCost += tempCost;
            }
            currentContainerCost = costPerDryTon * currentModifier.costModifier * currentContainerMass;

            currentContainerMass += zeroMassResourceMass;
        }

        private void internalUpdateVolumeUnits()
        {
            currentUsableVolume = (currentRawVolume - (currentRawVolume * tankageVolume)) * currentModifier.volumeModifier;
            float total = currentTotalVolumeRatio;
            int len = subContainerData.Length;
            float vol;
            for (int i = 0; i < len; i++)
            {
                vol = 0;
                if (total > 0)
                {
                    vol = subContainerData[i].volumeRatio / total;
                    vol *= currentUsableVolume;                    
                }
                subContainerData[i].setVolume(vol);
            }
        }

        private void internalUpdateTotalRatio()
        {
            currentTotalUnitRatio = 0;
            currentTotalVolumeRatio = 0;
            int len = subContainerData.Length;
            for (int i = 0; i < len; i++)
            {
                currentTotalUnitRatio += subContainerData[i].unitRatio;
                currentTotalVolumeRatio += subContainerData[i].volumeRatio;
            }
        }

        /// <summary>
        /// UNSAFE; MUST MANUALLY UPDATE AFTER CALLING THIS METHOD...
        /// </summary>
        private void internalClearRatios()
        {
            int len = subContainerData.Length;
            for (int i = 0; i < len; i++) { subContainerData[i].setRatio(0); }
        }

        private void internalInitializeDefaults()
        {
            if (!string.IsNullOrEmpty(defaultFuelPreset))
            {
                currentFuelPreset = defaultFuelPreset;
                setFuelPreset(internalGetFuelPreset(currentFuelPreset));
            }
            else//use default resource
            {
                internalClearRatios();
                currentFuelPreset = "custom";
                string[] splitResources = defaultResources.Split(',');
                if (splitResources.Length == 1) { splitResources = new string[] { splitResources[0], "1" }; }
                int len = splitResources.Length;
                for (int i = 0; i < len; i+=2)
                {
                    internalGetVolumeData(splitResources[i]).setRatio(int.Parse(splitResources[i+1]));
                }
                internalUpdateTotalRatio();
                internalUpdateVolumeUnits();
                internalUpdateMassAndCost();
            }
            resourcesDirty = true;
        }

        private SubContainerDefinition internalGetVolumeData(string resourceName) { return subContainersByName[resourceName]; }

        private ContainerFuelPreset internalGetFuelPreset(string name) { return Array.Find(fuelPresets, m => m.name == name); }

        private ContainerModifier internalGetModifier(string name) { return Array.Find(modifiers, m => m.name == name); }

    }

    /// <summary>
    /// Persistent data storage class for a single resource for a single container
    /// </summary>
    public class SubContainerDefinition
    {
        private readonly ContainerDefinition container;
        public readonly string name;//resource name
        private readonly PartResourceDefinition def;//resource definition
        private int ratio;//dimensionless ratio value        
        private float volume;//actual volume computed for this resource container from the total ratio

        public SubContainerDefinition(ContainerDefinition container, String name)
        {
            this.container = container;
            this.name = name;
            def = PartResourceLibrary.Instance.resourceDefinitions[name];
            if (def == null) { throw new NullReferenceException("Resource definition was null for name: " + name); }
        }

        public float resourceMass { get { return resourceUnits * unitMass; } }

        public float resourceCost { get { return resourceUnits * unitCost; } }

        /// <summary>
        /// Current cached usable volume occupied by this resource
        /// </summary>
        public float resourceVolume { get { return volume; } }

        /// <summary>
        /// Current number of units (volume/unitVolume) this subcontainer can hold
        /// </summary>
        public float resourceUnits { get { return volume / unitVolume; } }

        /// <summary>
        /// Current user or config specified unit ratio for this resource
        /// </summary>
        public int unitRatio { get { return ratio; } }

        /// <summary>
        /// Volume weighted ratio for this resource
        /// </summary>
        public float volumeRatio {get { return (float)ratio * unitVolume; }}

        /// <summary>
        /// Volume per-unit for this resource
        /// </summary>
        public float unitVolume { get { return FuelTypes.INSTANCE.getResourceVolume(name, def); } }

        /// <summary>
        /// Mass per-unit for this resource
        /// </summary>
        public float unitMass { get { return def.density; } }

        public float unitCost { get { return def.unitCost; } }
        
        public void addRatio(int addRatio)
        {
            setRatio(ratio + addRatio);
        }

        public void setVolume(float volume)
        {
            this.volume = volume;
        }

        public void setRatio(int ratio)
        {
            if (ratio < 0) { ratio = 0; }
            this.ratio = ratio;
            if (ratio == 0) { volume = 0; }
        }
    }

    /// <summary>
    /// Read-only data class for a set of modifiers to a tank based on different types of containers;
    /// </summary>
    public class ContainerModifier
    {
        public readonly string name;
        public readonly string title;
        public readonly string description;
        public readonly float volumeModifier = 0.85f;//applied before any resource volumes are calculated
        public readonly float dryMassModifier = 0.15f;//applied after dry mass for tank is tallied from resource mass fraction values
        public readonly float costModifier = 1f;
        public readonly float impactModifier = 1f;
        public readonly float heatModifier = 1f;
        public readonly float boiloffModifier = 1f;//in case of a 'semi' insulated container type this may be any value from 0-1
        public readonly float boiloffECConsumption = 1f;//modifier to the amount of EC needed to prevent boiloff
        public readonly bool useVolumeForMass = false;//special flag for structural tank type, to denote that dry mass is derived from raw volume rather than resource mass
        public ContainerModifier(ConfigNode node)
        {
            name = node.GetStringValue("name");
            title = node.GetStringValue("title");
            description = node.GetStringValue("description");
            volumeModifier = node.GetFloatValue("volumeModifier", volumeModifier);
            dryMassModifier = node.GetFloatValue("massModifier", dryMassModifier);
            costModifier = node.GetFloatValue("costModifier", costModifier);
            impactModifier = node.GetFloatValue("impactModifier", impactModifier);
            heatModifier = node.GetFloatValue("heatModifier", heatModifier);
            boiloffModifier = node.GetFloatValue("boiloffModifier", boiloffModifier);
            boiloffECConsumption = node.GetFloatValue("boiloffECModifier", boiloffECConsumption);
            useVolumeForMass = node.GetBoolValue("useVolumeForMass", useVolumeForMass);
        }
    }

    /// <summary>
    /// Simple wrapper around a set of resources to simplify the setup of custom container types for often-used groups of resources.
    /// </summary>
    public class ContainerResourceSet
    {
        public readonly string name;
        public readonly string[] availableResources;
        public readonly bool generic;//special flag for all pumpable resources
        public ContainerResourceSet(ConfigNode node)
        {
            name = node.GetStringValue("name");
            availableResources = node.GetStringValues("resource");
            generic = node.GetBoolValue("generic");
        }
    }

    /// <summary>
    /// Read-only data regarding a single fuel preset type.  A container may have multiple applicable preset types.<para/>
    /// These are used as a user-convenience setup option to simplify resource setup for often used fuel mixes.
    /// </summary>
    public class ContainerFuelPreset
    {
        public readonly string name;
        public readonly ContainerResourceRatio[] resourceRatios;
        public ContainerFuelPreset(ConfigNode node)
        {
            name = node.GetStringValue("name");
            ConfigNode[] ratioNodes = node.GetNodes("RESOURCE");
            int len = ratioNodes.Length;
            resourceRatios = new ContainerResourceRatio[len];
            for (int i = 0; i < len; i++)
            {
                resourceRatios[i] = new ContainerResourceRatio(ratioNodes[i]);
            }
        }

        /// <summary>
        /// Return true if this ContainerFuelPreset is applicable for the input list of resource names<para/>
        /// Return false if this fuel preset contains any resources that are not present in the input list of resource names<para/>
        /// Return false if this fuel preset contains no resource entries (empty/structural)
        /// </summary>
        /// <param name="availableResources"></param>
        /// <returns></returns>
        public bool applicable(string[] availableResources)
        {
            if (resourceRatios.Length == 0) { return false; }//structural, no preset button...
            bool valid = true;
            int len = resourceRatios.Length;
            for (int i = 0; i < len; i++)
            {
                if (!availableResources.Contains(resourceRatios[i].resourceName))
                {
                    valid = false;
                    break;
                }
            }
            return valid;
        }
    }

    /// <summary>
    /// Read-only data class for a ContainerFuelPreset ratio data for a single resource
    /// </summary>
    public class ContainerResourceRatio
    {
        public readonly string resourceName;
        public readonly int resourceRatio;
        public ContainerResourceRatio(ConfigNode node)
        {
            resourceName = node.GetStringValue("resource");
            resourceRatio = node.GetIntValue("ratio", 1);
        }
    }

    /// <summary>
    /// static class for utility methods related to loading container and resource data for volume-based containers
    /// </summary>
    public static class VolumeContainerLoader
    {
        private static ContainerModifier[] containerModifiers;
        private static ContainerResourceSet[] containerDefs;
        private static ContainerFuelPreset[] containerPresets;

        public static void loadConfigs()
        {
            ConfigNode[] typeNodes = GameDatabase.Instance.GetConfigNodes("SSTU_CONTAINERTYPE");
            int len = typeNodes.Length;
            containerModifiers = new ContainerModifier[len];
            for (int i = 0; i < len; i++) { containerModifiers[i] = new ContainerModifier(typeNodes[i]); }

            ConfigNode[] defNodes = GameDatabase.Instance.GetConfigNodes("SSTU_RESOURCESET");
            len = defNodes.Length;
            containerDefs = new ContainerResourceSet[len];
            for (int i = 0; i < len; i++) { containerDefs[i] = new ContainerResourceSet(defNodes[i]); }

            ConfigNode[] presetNodes = GameDatabase.Instance.GetConfigNodes("SSTU_FUELTYPE");
            len = presetNodes.Length;
            containerPresets = new ContainerFuelPreset[len];
            for (int i = 0; i < len; i++) { containerPresets[i] = new ContainerFuelPreset(presetNodes[i]); }
        }

        public static string[] getAllModifierNames()
        {
            int len = containerModifiers.Length;
            string[] names = new string[len];
            for (int i = 0; i < len; i++) { names[i] = containerModifiers[i].name; }
            return names;
        }

        public static ContainerModifier[] getModifiersByName(string[] names)
        {
            List<ContainerModifier> mods = new List<ContainerModifier>();
            int len = containerModifiers.Length;
            for (int i = 0; i < len; i++)
            {
                if (names.Contains(containerModifiers[i].name))
                {
                    mods.Add(containerModifiers[i]);
                }
            }
            return mods.ToArray();
        }

        public static ContainerModifier[] getConainerTypes() { return containerModifiers; }

        public static ContainerModifier getContainerType(String name) { return Array.Find(containerModifiers, m => m.name == name); }

        public static ContainerResourceSet getResourceSet(String name) { return Array.Find(containerDefs, m => m.name == name); }

        public static ContainerFuelPreset[] getPresets() { return containerPresets; }

        public static ContainerFuelPreset getPreset(String name) { return Array.Find(containerPresets, m => m.name == name); }

    }
}