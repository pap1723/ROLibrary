﻿using ferram4;
using FerramAerospaceResearch.FARAeroComponents;
using ProceduralParts;
using RealFuels.Tanks;
using WingProcedural;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KerbalConstructionTime;


namespace ROLib
{
    public class ModuleROMaterials : PartModule, IPartMassModifier, IPartCostModifier
    {
        private const string GroupDisplayName = "RO-Thermal_Protection";
        private const string GroupName = "ModuleROMaterials";

        #region KSPFields

        [KSPField(isPersistant = true, guiName = "Core", guiActiveEditor = true, groupName = GroupName, groupDisplayName = GroupDisplayName), 
         UI_ChooseOption(scene = UI_Scene.Editor, suppressEditorShipModified = true)]
        public string presetCoreName = "";
        [KSPField(isPersistant = true, guiName = "TPS", guiActiveEditor = true, groupName = GroupName, groupDisplayName = GroupDisplayName), 
         UI_ChooseOption(scene = UI_Scene.Editor, suppressEditorShipModified = true)]
        public string presetSkinName = "";
        [KSPField(isPersistant = true, guiName = "TPS height (mm)", guiActiveEditor = true, groupName = GroupName, groupDisplayName = GroupDisplayName), 
         UI_FloatEdit(sigFigs = 1, suppressEditorShipModified = true)]
        public float tpsHeightDisplay = 1.0f;
        [KSPField(isPersistant = true, guiName = "Core", guiActiveEditor = true, groupName = GroupName, groupDisplayName = GroupDisplayName)]
        public string presetCoreNameAltDispl = "";
        [KSPField(guiActiveEditor = true, guiName = "Desc", groupName = GroupName, groupDisplayName = GroupDisplayName)]
        public string description = "";
        [KSPField(guiActiveEditor = true, guiName = "Temp", guiUnits = "K", groupName = GroupName, groupDisplayName = GroupDisplayName)]
        public string maxTempDisplay = "";
        [KSPField(guiActiveEditor = true, guiName = "Heat Capacity",  groupName = GroupName, groupDisplayName = GroupDisplayName)]
        public string thermalMassDisplay = "";
        [KSPField(guiActiveEditor = true, guiName = "Thermal Insulance" , groupName = GroupName, groupDisplayName = GroupDisplayName)]
        public string thermalInsulanceDisplay = "";
        [KSPField(guiActiveEditor = true, guiName = "Emissivity",  groupName = GroupName, groupDisplayName = GroupDisplayName)]
        public string emissiveConstantDisplay = "";
        [KSPField(guiActiveEditor = true, guiName = "Mass", groupName = GroupName, groupDisplayName = GroupDisplayName)]
        public String massDisplay = "";
        [KSPField(guiActiveEditor = true, guiName = "Skin Density", guiFormat = "F3", guiUnits = "kg/m²",  groupName = GroupName, groupDisplayName = GroupDisplayName)]
        public float surfaceDensityDisplay = 0.0f;

        [KSPField(isPersistant = true, guiName = "Max Temp", guiActive = true, guiActiveEditor = false, guiActiveUnfocused = false)]
        public string FlightDisplay = "";
        [KSPField(isPersistant = true, guiName = "Exps", guiActive = true, guiActiveEditor = false, guiActiveUnfocused = false)]
        public string FlightDebug = "";

        #endregion KSPFields

        #region Private Variables

        private ModuleAblator modAblator;
        private ModuleROTank modularPart;
        private ProceduralPart proceduralPart;
        private ModuleFuelTanks moduleFuelTanks;
        private FARAeroPartModule fARAeroPartModule; 
        private FARWingAerodynamicModel fARWingModule;
        private WingProcedural.WingProcedural wingProceduralModule;
        private ModuleTagList CCTagListModule;
        private PresetROMatarial presetCore;
        private PresetROMatarial presetTPS;
        
        private const string reentryTag = "Reentry";
        private bool reentryByDefault = false;
        private float tpsCost = 0.0f;
        private float tpsMass = 0.0f;
        private double tpsSurfaceDensity = 0.0f; 
        private double skinIntTransferCoefficient = 0.0;
        private double thermalMassAreaBefore = 0.0f;
        private float moduleMass = 0.0f;
        private float partMassCached = 0.0f;
        private string ablatorResourceName;
        private string outputResourceName;
        private bool onLoadFiredInEditor;
        private bool ignoreSurfaceAttach = true; // ignore all surface attached parts/childern when subtracting surface area
        private string[] ignoredNodes = new string[] {}; // ignored Nodes when subtracting surface area
        private float prevHeight = -10.001f;
        private double heatConductivityDiv = 1f / (10.0 * PhysicsGlobals.ConductionFactor );
        private double SkinInternalConductivityDiv = 1f / (10.0 * PhysicsGlobals.ConductionFactor * PhysicsGlobals.SkinInternalConductionFactor * 0.5);
        private double SkinSkinConductivityDiv = 1f / (10.0 * PhysicsGlobals.ConductionFactor * PhysicsGlobals.SkinSkinConductionFactor);
        private double absorptiveConstantOrig;
        
        [SerializeField] private string[] availablePresetNamesCore = new string[] {};
        [SerializeField] private string[] availablePresetNamesSkin = new string[] {};
        
        // TODO need detailed implementation
        //
        // HeatConductivity seems to be a replacement value for thermal contact resistance in inverse
        // which then get multltiplied together
        // since heat transfer calculations usualy add them together & don't multiply them, like the game does 
        // flux: Q = U * A * ΔT     U [kW/(m²·K)]: overall heat transfer coefficient 
        //                          U = 1 /( l1/k1 + l2/k2 + 1/hc) bc. temperatures inside parts are uniform l1&2 get infinitely small
        //                          U = hc      in ksp U -> part.heatConductivity * part2.heatConductivity * global mult
        //                          Al->Al@1atm ~2200 hc
        // partThermalData.localIntConduction[kW] += thermalLink.contactArea[m^2] * thermalLink.temperatureDelta[K]
        //                                       * thermalLink.remotePart.heatConductivity[Sqrt(kW/(m²·K))] * part.heatConductivity[Sqrt(kW/(m²·K))]
        //                                       * PhysicsGlobals.ConductionFactor * 10.0

        // TODO need detailed implementation
        private double SkinSkinConductivity {
            get {
                if (presetTPS.skinSkinConductivity > 0) {
                    return presetTPS.skinSkinConductivity / part.heatConductivity * SkinSkinConductivityDiv;
                } else if (presetCore.skinSkinConductivity > 0 ) {
                    return presetCore.skinSkinConductivity / part.heatConductivity * SkinSkinConductivityDiv;
                } else {
                    return part.partInfo.partPrefab.skinSkinConductionMult;
                }
            }
        }

        #endregion Private Variables
        [KSPField] public float surfaceAreaPart = -0.1f;
        [KSPField] public float volumePart = -0.1f;
        [KSPField] public bool tpsMassIsAdditive = true;
        [KSPField] public double surfaceArea = 0.0; // m2
        [KSPField] public double surfaceAreaCovered = 0.0; // m2
        [Persistent] public string coreCfg = "";
        [Persistent] public string skinCfg = "";
        [Persistent] public float skinHeightCfg = -1.0f;
        public float TPSAreaCost => presetTPS?.costPerArea ?? 1.0f;
        public float TPSAreaMult => presetTPS?.heatShieldAreaMult ?? 1.0f;

        public float CurrentDiameter => modularPart?.currentDiameter ?? 0f;
        public float LargestDiameter => modularPart?.largestDiameter ?? 0f;
        public float TotalTankLength => modularPart?.totalTankLength ?? 0f;
        public string PresetCore {
            get{ return presetCore.name; }
            set{ 
                if (PresetROMatarial.PresetsCore.TryGetValue(value, out PresetROMatarial preset)) {
                    presetCoreName = value;
                    presetCoreNameAltDispl = value;
                    presetCore = preset;
                }
                else if (coreCfg != "" & PresetROMatarial.PresetsSkin.TryGetValue(coreCfg, out preset))
                {
                    Debug.LogError($"[ROThermal] " + part.name + " Preset " + presetCoreName + " config not available, Faling back to" + coreCfg);
                    presetCoreName = coreCfg;
                    presetCoreNameAltDispl = coreCfg;
                    presetCore = preset;
                }
                else
                {
                    Debug.LogError($"[ROThermal] " + part.name + " Preset " + presetCoreName + " config not available, Faling back to default");
                    PresetROMatarial.PresetsSkin.TryGetValue("default", out preset);
                    presetCoreName = "default";
                    presetCoreNameAltDispl = "default";
                    presetCore = preset;
                }
            }
        }
        public string PresetTPS {
            get{ return presetTPS.name; }
            set{ 
                if (PresetROMatarial.PresetsSkin.TryGetValue(value, out PresetROMatarial preset)) {
                    presetSkinName = value;
                    presetTPS = preset;
                }  
                else if (skinCfg != "" & PresetROMatarial.PresetsSkin.TryGetValue(skinCfg, out preset))
                {
                    Debug.LogError($"[ROThermal] " + part.name + " Preset " + presetSkinName + " config not available, Faling back to" + skinCfg);
                    presetSkinName = value;
                    presetTPS = preset;
                }
                else
                {
                    Debug.LogError($"[ROThermal] " + part.name + " Preset " + presetSkinName + " config not available, Faling back to None");
                    PresetROMatarial.PresetsSkin.TryGetValue("None", out preset);
                    presetSkinName = value;
                    presetTPS = preset;
                }
            }      
        }
        private float SkinHeightMaxVal 
        {
            get {
                if (presetTPS.skinHeightMax > 0.0f) {
                    return (float)presetTPS.skinHeightMax * 1000f;
                }
                return 0;
            }
        }

        public double SetSurfaceArea ()
        {
                if (fARAeroPartModule != null) 
                {
                    // Inconsistant results on procedural parts with Editor & Flight, returned results for a cylinder are much closer to a cube
                    // Procedural Tank
                    // 3x3 cylinder 42.4m^2 -> surfaceArea = 52.5300 (In Editor) 77.36965 (In Flight)
                    // 3x3x3 cube   54.0m^2 -> surfaceArea = 56.3686 (In Editor) 71.05373 (In Flight)

                    surfaceArea = fARAeroPartModule?.ProjectedAreas.totalArea ?? 0.0f;
                    
                    if (surfaceArea > 0.0)
                    {
                        // Debug.Log("[ROThermal] get_SurfaceArea derived from fARAeroPartModule: " + surfaceArea);
                        surfaceAreaCovered =  surfaceArea;
                    } 
                    else 
                    {
                        if (fARAeroPartModule?.ProjectedAreas == null)
                            Debug.Log("[ROThermal] get_SurfaceArea skipping fARAeroPartModule ProjectedAreas = null ");
                        else if (fARAeroPartModule?.ProjectedAreas.totalArea == null)
                            Debug.Log("[ROThermal] get_SurfaceArea skipping fARAeroPartModule totalArea = null ");
                        else
                            Debug.Log("[ROThermal] get_SurfaceArea skipping fARAeroPartModule got " + surfaceArea);
                    }
                }
                else if (surfaceAreaPart > 0.0f) 
                {
                    Debug.Log("[ROThermal] get_SurfaceArea derived from SurfaceAreaPart Entry: " + surfaceAreaPart);
                    surfaceAreaCovered =  surfaceAreaPart;
                }
                else if (wingProceduralModule is WingProcedural.WingProcedural & fARWingModule != null)
                {
                    // TODO preciser calculation needed
                    Debug.Log("[ROThermal] get_SurfaceArea deriving from b9wingProceduralModule: ");
                    surfaceArea = (float)fARWingModule.S * 2 + (wingProceduralModule.sharedBaseThicknessRoot + wingProceduralModule.sharedBaseThicknessTip)
                            * Mathf.Atan((wingProceduralModule.sharedBaseWidthRoot + wingProceduralModule.sharedBaseWidthTip) / (float)fARWingModule.b_2_actual);
                            // aproximation for leading & trailing Edge
                    Debug.Log("[ROThermal] get_SurfaceArea derived from ModuleWingProcedural: " + surfaceArea);
                    surfaceAreaCovered =  surfaceArea;
                }
                else if (modularPart is ModuleROTank)
                {
                    surfaceArea =  Mathf.PI / 2f * ((CurrentDiameter + LargestDiameter) * TotalTankLength + (CurrentDiameter * CurrentDiameter + LargestDiameter * LargestDiameter) / 2f);
                    Debug.Log("[ROThermal] get_SurfaceArea derived from ModuleROTank: " + surfaceArea);
                    surfaceAreaCovered =  surfaceArea;
                }
                else if (proceduralPart is ProceduralPart)
                {
                    Debug.Log("[ROThermal] get_SurfaceArea deriving from ProceduralPart: ");
                    surfaceArea =  proceduralPart.SurfaceArea;
                    Debug.Log("[ROThermal] get_SurfaceArea derived from ProceduralPart: " + surfaceArea);
                    surfaceAreaCovered =  surfaceArea;
                }


                /// decrease surface area based on contact area of attached nodes & surface attached parts
                if (surfaceAreaCovered > 0.0) 
                {
                    //part.DragCubes.SetPartOcclusion();
                    part.DragCubes.ForceUpdate(false, true, false);
                    //Debug.Log($"[ROThermal] part.DragCubes.SetPartOcclusion() ");
                    string str = $"[ROThermal] get_SurfaceArea Surface Area: " + surfaceAreaCovered + " coverd skin: attachNodes ";
                    foreach (AttachNode nodeAttach in part.attachNodes) 
                    {
                        if (nodeAttach.attachedPart == null | ignoredNodes.Contains(nodeAttach.id))
                            continue;
                        nodeAttach.attachedPart.DragCubes.ForceUpdate(false, true, false);
                        surfaceAreaCovered -= nodeAttach.contactArea;
                        str += nodeAttach.contactArea + ", ";
                    }
                    part.srfAttachNode.attachedPart?.DragCubes.SetPartOcclusion();
                    str +=  "srfAttachNode " + part.srfAttachNode.contactArea + ", ";
                    surfaceAreaCovered -= part.srfAttachNode?.contactArea ?? 0.0f;
                    if (!ignoreSurfaceAttach)
                    {   
                        str +=  "children ";
                        Debug.Log($"[ROThermal] part.srfAttachNode.contactArea ");
                        foreach (Part child in part.children) 
                        {
                            if (child == null)
                                continue;
                            child.DragCubes.RequestOcclusionUpdate();
                            child.DragCubes.ForceUpdate(false, true, false);
                            child.DragCubes.SetPartOcclusion();
                            str +=  child.srfAttachNode.contactArea + ", ";
                            surfaceAreaCovered -= child.srfAttachNode.contactArea;
                        }
                    }
                    //Debug.Log(str + "  Result: " +  surfaceAreaCovered);
                }
                if (surfaceAreaCovered > 0.0)
                    return surfaceAreaCovered;
                Debug.LogWarning("[ROThermal] get_SurfaceArea failed: Area=" + surfaceAreaCovered);
                return 0f;
        }
        
        public float TPSMass => (float)(surfaceArea * tpsSurfaceDensity * TPSAreaMult / 1000f);
        public float TPSCost => (float)surfaceArea * TPSAreaCost * TPSAreaMult;

        public float Ablator => Mathf.Round((float)surfaceArea * presetTPS.heatShieldAblator * 10f) / 10f;       
        private static bool? _RP1Found = null;
        public static bool RP1Found
        {
            get
            {
                if (!_RP1Found.HasValue)
                {
                    var assembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "RP0")?.assembly;
                    _RP1Found = assembly != null;
                }
                return _RP1Found.Value;
            }
        }

        #region Standard KSP Overrides

        // Remove after Debugging is less needed
        private double tick = 470.0;
        private double peakTempSkin = 0.0;
        private double peakTemp;
        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight) {
                if (tick % 50 == 0) {
                    if ( part.temperature > peakTemp)
                        peakTemp = part.temperature;
                    if (part.skinTemperature > peakTempSkin)
                        peakTempSkin = part.skinTemperature;

                    FlightDebug =   " Temp " + String.Format("{0000:0}", part.skinTemperature) + "K Exposed AreaFrac " + String.Format("{0:0.###}", part.skinExposedAreaFrac)
                                     + "\nUnexp Temp " + String.Format("{0000:0}", part.skinUnexposedTemperature) + "K"
                                     //+ "\nArea P exposed " + String.Format("{0:0.###}",part.exposedArea) + " P radiative " + String.Format("{0:0.###}",part.radiativeArea) 
                                     //+ "\nSkin exposed " + String.Format("{0:0.###}",part.skinExposedArea) 
                                     + "\nconvection AreaMult " + String.Format("{0:0.###}", part.ptd.convectionAreaMultiplier)
                                     + " TempMult " + String.Format("{0:0.###}", part.ptd.convectionTempMultiplier)
                                     + "\nbkg rad " + String.Format("{0:0.#}", part.ptd.brtUnexposed) + " exposed " + String.Format("{0:0.#}", part.ptd.brtExposed)
                                     + "\nPeak Temp Skin" + String.Format("{0:0.}", peakTempSkin)+ "K Core " + String.Format("{0:0.}", peakTemp) +"K";                         
                }
                if (tick % 500 == 0) {
                    DebugLog();
                }
                tick ++;
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            onLoadFiredInEditor = HighLogic.LoadedSceneIsEditor;
            heatConductivityDiv = 1.0 / (10.0 * PhysicsGlobals.ConductionFactor );
            SkinInternalConductivityDiv = heatConductivityDiv / ( PhysicsGlobals.SkinInternalConductionFactor * 0.5);
            SkinSkinConductivityDiv = heatConductivityDiv / PhysicsGlobals.SkinSkinConductionFactor;

            node.TryGetValue("TPSSurfaceArea", ref surfaceAreaPart);
            node.TryGetValue("Volume", ref volumePart);
            if (!node.TryGetValue("skinMassIsAdditive", ref tpsMassIsAdditive))
                Debug.LogWarning("[ROThermal] "+ part.name + " skinMassAdditive entry not found");
            
            if (node.TryGetValue("corePresets", ref availablePresetNamesCore))
                Debug.Log("[ROThermal] available presetsCore loaded");
            if (node.TryGetValue("skinPresets", ref availablePresetNamesSkin))
                Debug.Log("[ROThermal] available presetsSkin loaded");  
            node.TryGetValue("ignoreNodes", ref ignoredNodes);
            node.TryGetValue("ignoreSurfaceAttach", ref ignoreSurfaceAttach);

            node.TryGetValue("core", ref coreCfg);
            node.TryGetValue("skin", ref skinCfg);
            node.TryGetValue("skinHeight", ref skinHeightCfg);

            ensurePresetIsInList(ref availablePresetNamesCore, coreCfg);
            ensurePresetIsInList(ref availablePresetNamesSkin, skinCfg);
        }

        public override void OnStart(StartState state)
        {
            Fields[nameof(presetCoreName)].guiActiveEditor = false;
            Fields[nameof(presetCoreNameAltDispl)].guiActiveEditor = true;
            
            PresetROMatarial.LoadPresets();
            if (!PresetROMatarial.Initialized)
                return;

            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)
            {
                if (presetCoreName != "") 
                    PresetCore = presetCoreName;
                else if (coreCfg != "")
                    PresetCore = coreCfg;
                else
                    PresetCore = "default";

                if (presetSkinName != "") 
                    PresetTPS = presetSkinName;
                else if (skinCfg != "")
                    PresetTPS = skinCfg;
                else
                    PresetTPS = "None";
            }


            if (HighLogic.LoadedSceneIsEditor) {
                
                if (availablePresetNamesCore.Length > 0)
                { 
                    // RP-1 allows selecting all configs but will run validation when trying to add the vessel to build queue
                    string[] unlockedPresetsName = RP1Found ? availablePresetNamesCore : GetUnlockedPresets(availablePresetNamesCore);

                    UpdatePresetsList(unlockedPresetsName, PresetType.Core);
                }          

                Fields[nameof(presetCoreName)].uiControlEditor.onFieldChanged = 
                Fields[nameof(presetCoreName)].uiControlEditor.onSymmetryFieldChanged =
                    (bf, ob) => ApplyCorePreset(presetCoreName);

                /*if (!onLoadFiredInEditor)
                {
                    EnsureBestAvailableConfigSelected();
                }*/
                

                if (availablePresetNamesSkin.Length > 0)
                {
                    // GameEvents.onEditorShipModified.Add(OnEditorShipModified);
                    // RP-1 allows selecting all configs but will run validation when trying to add the vessel to build queue
                    string[] unlockedPresetsName = RP1Found ? availablePresetNamesSkin : GetUnlockedPresets(availablePresetNamesSkin);
                    UpdatePresetsList(unlockedPresetsName, PresetType.Skin);
                }
                
                Fields[nameof(presetSkinName)].uiControlEditor.onFieldChanged =
                Fields[nameof(presetSkinName)].uiControlEditor.onSymmetryFieldChanged =
                    (bf, ob) => ApplySkinPreset(presetSkinName, true);
                Fields[nameof(tpsHeightDisplay)].uiControlEditor.onFieldChanged =
                Fields[nameof(tpsHeightDisplay)].uiControlEditor.onSymmetryFieldChanged = 
                    (bf, ob) => OnHeightChanged(tpsHeightDisplay);
                
                this.ROLupdateUIFloatEditControl(nameof(tpsHeightDisplay), (float)presetTPS.skinHeightMin * 1000f, SkinHeightMaxVal, 10f, 1f, 0.01f);
            }
        } 

        public override void OnStartFinished(StartState state)
        {
            Debug.Log($"[ROThermal] " + part.name + " OnStartFinished() LoadedScene is " + HighLogic.LoadedScene);
            base.OnStartFinished(state);

            absorptiveConstantOrig = part.absorptiveConstant;

            if (!PresetROMatarial.Initialized)
                return;

            modAblator = part.FindModuleImplementing<ModuleAblator>();
            modularPart = part?.FindModuleImplementing<ModuleROTank>();
            proceduralPart = part?.FindModuleImplementing<ProceduralPart>();
            moduleFuelTanks = part?.FindModuleImplementing<ModuleFuelTanks>();
            fARAeroPartModule = part?.FindModuleImplementing<FARAeroPartModule>();
            fARWingModule = part?.FindModuleImplementing<FARControllableSurface>();
            fARWingModule = part?.FindModuleImplementing<FARWingAerodynamicModel>();
            wingProceduralModule = part?.FindModuleImplementing<WingProcedural.WingProcedural>();
            CCTagListModule = part?.FindModuleImplementing<ModuleTagList>();

            if(HighLogic.LoadedSceneIsEditor) {
                if (moduleFuelTanks is ModuleFuelTanks)
                { 
                    /// ModuleFuelTanks changes TankType & mass on Update()
                    moduleFuelTanks.Fields[nameof(moduleFuelTanks.typeDisp)].uiControlEditor.onFieldChanged += (bf, ob) => UpdateCoreForRealfuels(true);
                    moduleFuelTanks.Fields[nameof(moduleFuelTanks.typeDisp)].uiControlEditor.onSymmetryFieldChanged += (bf, ob) => UpdateCoreForRealfuels(true);
                    GameEvents.onPartResourceListChange.Add(onPartResourceListChange);
                    Debug.Log("[ROThermal] " + part.name + " ModuleFuelTanks found " + moduleFuelTanks.name + " updating core material list");
                    UpdateCoreForRealfuels(false);
                }
                if (EditorLogic.RootPart != null & (fARWingModule != null | moduleFuelTanks is ModuleFuelTanks))
                    EditorCordinator.AddToMassCheckList(this);
                    
                ApplyCorePreset(presetCoreName);
                ApplySkinPreset(presetSkinName, false);
            }
            if(CCTagListModule is ModuleTagList & CCTagListModule.tags.Contains(reentryTag))
            {
                reentryByDefault = true;
            }

            if (HighLogic.LoadedSceneIsFlight) {
                ApplyCorePreset(presetCoreName);
                ApplySkinPreset(presetSkinName, false);
            }
            if (HighLogic.LoadedSceneIsFlight)
                DebugLog();
        }
        
        private void OnDestroy()
        {
            EditorCordinator.RemoveToMassCheckList(this);
            if (moduleFuelTanks is ModuleFuelTanks & HighLogic.LoadedSceneIsEditor)
                GameEvents.onPartResourceListChange.Add(onPartResourceListChange);
        }

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => moduleMass;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => tpsCost - defaultCost;

        #endregion Standard KSP Overrides


        #region Custom Methods


        /// <summary>
        /// Message sent from ProceduralPart, ProceduralWing or ModuleROTanks when it updates.
        /// </summary>
        [KSPEvent]
        public void OnPartVolumeChanged(BaseEventDetails data)
        {
            Debug.Log($"[ROThermal] OnPartVolumeChanged Message caught");
            if (!HighLogic.LoadedSceneIsEditor) return;
            UpdateGeometricProperties();
        }
        public void onPartResourceListChange(Part dPart)
        {
            Debug.Log($"[ROThermal] onPartResourceListChange Part {part} Message caught.");
            checkMassUpdate(true);
        }

        public bool checkMassUpdate(bool recheck = true, int ntry = 0)
        {
            if(part.mass == partMassCached)
                return false;

            Debug.Log($"[ROThermal] checkMass Part {part} mass updated after {ntry} fixedUpdates new value {part.mass}, old {partMassCached}");
            partMassCached = part.mass;
            UpdateGUI();
            return true;
        }

        public bool TrySurfaceAreaUpdate(int ntry)
        {
            //Debug.Log($"[ROThermal] UpdateSurfaceArea Instance created surface area Part {surfaceArea}, fAR ProjectedArea {fARAeroPartModule?.ProjectedAreas.totalArea}");
            if (fARAeroPartModule.ProjectedAreas.totalArea < 0.000001)
                return false;
            if (surfaceArea == fARAeroPartModule.ProjectedAreas.totalArea)
                return false;
                
            Debug.Log($"[ROThermal] UpdateSurfaceArea updating surface Area after {ntry} fixedUpdates");
            UpdateGeometricProperties();
            return true;
        }
        public void OnHeightChanged(float height) {
            if (height == prevHeight) return;

            UpdateHeight();
            ApplyThermal();
            UpdateGeometricProperties();
        }

        public void UpdateHeight(bool newSkin = false) {
            float heightMax = SkinHeightMaxVal; 
            float heightMin = (float)presetTPS.skinHeightMin * 1000f;

            if (newSkin == true) {
                if (heightMax < heightMin)
                {
                    Debug.LogWarning($"[ROThermal] Warning Preset "+ presetTPS.name + " skinHeightMax lower then skinHeightMin");
                    tpsHeightDisplay = heightMin;
                }
                else if (thermalMassAreaBefore != 0.0) 
                {
                    double mult = PhysicsGlobals.StandardSpecificHeatCapacity * part.thermalMassModifier;

                    double heightfactor  = (thermalMassAreaBefore -  presetTPS.skinSpecificHeatCapacity * presetTPS.skinMassPerArea / mult)
                                            / (presetTPS.skinMassPerAreaMax - presetTPS.skinMassPerArea 
                                               + presetTPS.skinMassPerArea * (presetTPS.skinSpecificHeatCapacityMax - presetTPS.skinSpecificHeatCapacity) / mult);
                    

                    tpsHeightDisplay = 1000.0f * (float)(heightfactor * (presetTPS.skinHeightMax - presetTPS.skinHeightMin) + presetTPS.skinHeightMin);

                    Debug.Log($"[ROThermal] UpdateHeight(New Skin) tpsHeightDisplay {tpsHeightDisplay}, heightfactor3 {heightfactor}" );

                    tpsHeightDisplay = Mathf.Clamp(tpsHeightDisplay, heightMin, heightMax);
                }
                else if (presetSkinName == skinCfg & skinHeightCfg > 0.0f) {
                    tpsHeightDisplay = Mathf.Clamp(skinHeightCfg, heightMin, heightMax);
                }
                else {
                    tpsHeightDisplay = (float)Math.Round((double)((heightMax - heightMin) * 0.75f + heightMin), 1);
                }
                this.ROLupdateUIFloatEditControl(nameof(tpsHeightDisplay), heightMin, heightMax, 10f, 1f, 0.1f);
            }
            else
            {
                tpsHeightDisplay = Mathf.Clamp(tpsHeightDisplay, heightMin, heightMax);
            }
            prevHeight = tpsHeightDisplay;
        }

        public void ApplyCorePreset (string preset) {
            PresetCore = preset;

            // maxTemp
            if (presetCore.maxTempOverride > 0) {
                part.maxTemp = presetCore.maxTempOverride;
            } else {
                part.maxTemp = part.partInfo.partPrefab.maxTemp;
            }    
            // thermalMassModifier
            if (presetCore.specificHeatCapacity > 0) {
                part.thermalMassModifier = presetCore.specificHeatCapacity / PhysicsGlobals.StandardSpecificHeatCapacity;
            } else {
                part.thermalMassModifier = part.partInfo.partPrefab.thermalMassModifier;
            }

            ApplyThermal();
            CCTagUpdate(presetCore);

            Debug.Log($"[ROThermal] applied preset {PresetCore} for part {part.name}");     
            UpdateGeometricProperties();
        }
        public void ApplySkinPreset (string preset, bool skinNew) {
            PresetTPS = preset;
            UpdateHeight(skinNew);
            ApplyThermal();
            CCTagUpdate(presetTPS);

            // update ModuleAblator parameters, if present and used
            if (modAblator != null && !presetTPS.disableModAblator)
            {
                if (!string.IsNullOrWhiteSpace(presetTPS.AblativeResource))
                    modAblator.ablativeResource = presetTPS.AblativeResource;
                if (!string.IsNullOrWhiteSpace(presetTPS.OutputResource))
                    modAblator.outputResource = presetTPS.OutputResource;

                if (!string.IsNullOrWhiteSpace(presetTPS.NodeName))
                    modAblator.nodeName = presetTPS.NodeName;
                if (!string.IsNullOrWhiteSpace(presetTPS.CharModuleName))
                    modAblator.charModuleName = presetTPS.CharModuleName;
                if (!string.IsNullOrWhiteSpace(presetTPS.UnitsName))
                    modAblator.unitsName = presetTPS.UnitsName;

                if (presetTPS.LossExp.HasValue)
                    modAblator.lossExp = presetTPS.LossExp.Value;
                if (presetTPS.LossConst.HasValue)
                    modAblator.lossConst = presetTPS.LossConst.Value;
                if (presetTPS.PyrolysisLossFactor.HasValue)
                    modAblator.pyrolysisLossFactor = presetTPS.PyrolysisLossFactor.Value;
                if (presetTPS.AblationTempThresh.HasValue)
                    modAblator.ablationTempThresh = presetTPS.AblationTempThresh.Value;
                if (presetTPS.ReentryConductivity.HasValue)
                    modAblator.reentryConductivity = presetTPS.ReentryConductivity.Value;
                if (presetTPS.UseNode.HasValue)
                    modAblator.useNode = presetTPS.UseNode.Value;
                if (presetTPS.CharAlpha.HasValue)
                    modAblator.charAlpha = presetTPS.CharAlpha.Value;
                if (presetTPS.CharMax.HasValue)
                    modAblator.charMax = presetTPS.CharMax.Value;
                if (presetTPS.CharMin.HasValue)
                    modAblator.charMin = presetTPS.CharMin.Value;
                if (presetTPS.UseChar.HasValue)
                    modAblator.useChar = presetTPS.UseChar.Value;
                if (presetTPS.OutputMult.HasValue)
                    modAblator.outputMult = presetTPS.OutputMult.Value;
                if (presetTPS.InfoTemp.HasValue)
                    modAblator.infoTemp = presetTPS.InfoTemp.Value;
                if (presetTPS.Usekg.HasValue)
                    modAblator.usekg = presetTPS.Usekg.Value;
                if (presetTPS.NominalAmountRecip.HasValue)
                    modAblator.nominalAmountRecip = presetTPS.NominalAmountRecip.Value;
            }

            if (modAblator != null)
            {
                if (presetTPS.AblativeResource == null || ablatorResourceName !=presetTPS.AblativeResource ||
                    presetTPS.OutputResource == null || outputResourceName != presetTPS.OutputResource ||
                    presetTPS.disableModAblator)
                {
                    RemoveAblatorResources();
                }

                ablatorResourceName = presetTPS.AblativeResource;
                outputResourceName = presetTPS.OutputResource;

                modAblator.isEnabled = modAblator.enabled = !presetTPS.disableModAblator;
            }

            if (!string.IsNullOrEmpty(presetTPS.description))
            {
                Fields[nameof(description)].guiActiveEditor = true;
                description = presetTPS.description;
            }
            else
                Fields[nameof(description)].guiActiveEditor = false;

            Debug.Log($"[ROThermal] applied preset {PresetTPS} for part {part.name}");     
            UpdateGeometricProperties();
        }

        public void ApplyThermal()
        // part.skinInternalConductionMult = skinIntTransferCoefficient * [1 / conductionMult] / part.heatConductivity
        //
        // skinInteralConductionFlux[kJ/s] = InternalConductivity[kJ/s-K] * FluxExponent[] * dT[K]
        // InternalConductivity[kJ/s-K]    = part.skinExposedAreaFrac[] * part.radiativeArea[m^2] * part.skinInternalConductionMult[kJ/(s*m^2*K)] * conductionMult[] * part.heatConductivity
        // conductionMult                  = PhysicsGlobals.SkinInternalConductionFactor * 0.5 * PhysicsGlobals.ConductionFactor * 10
        // skinIntTransferCoefficient[kW/(m^2*K)] = 1 / ThermalResistance [(m^2*K)/kW]
        //
        {
            // heatConductivity
            if (presetTPS.thermalConductivity > 0) {
                part.heatConductivity = presetTPS.thermalConductivity * heatConductivityDiv;
            } else if (presetCore.thermalConductivity > 0 ) {
                part.heatConductivity = presetCore.thermalConductivity * heatConductivityDiv;
            } else {
                part.heatConductivity = part.partInfo.partPrefab.heatConductivity;
            };

            if (presetTPS.skinHeightMax > 0.0 & presetTPS.skinSpecificHeatCapacityMax > 0.0 & presetTPS.skinMassPerAreaMax > 0.0) 
            {
                double heightfactor = (tpsHeightDisplay / 1000 - presetTPS.skinHeightMin) / (presetTPS.skinHeightMax - presetTPS.skinHeightMin);

                tpsSurfaceDensity = (presetTPS.skinMassPerAreaMax - presetTPS.skinMassPerArea) * heightfactor + presetTPS.skinMassPerArea;
                part.skinMassPerArea = tpsSurfaceDensity;
                part.skinThermalMassModifier = ((presetTPS.skinSpecificHeatCapacityMax - presetTPS.skinSpecificHeatCapacity) * heightfactor + presetTPS.skinSpecificHeatCapacity)
                                                / PhysicsGlobals.StandardSpecificHeatCapacity / part.thermalMassModifier;
                skinIntTransferCoefficient = (presetTPS.skinIntTransferCoefficientMax - presetTPS.skinIntTransferCoefficient) * heightfactor + presetTPS.skinIntTransferCoefficient;
                part.skinInternalConductionMult = skinIntTransferCoefficient * SkinInternalConductivityDiv / part.heatConductivity ;
            } 
            else 
            {
                // skinMassPerArea
                if (presetTPS.skinMassPerArea > 0.0) {
                    part.skinMassPerArea = presetTPS.skinMassPerArea;
                    tpsSurfaceDensity = (float)presetTPS.skinMassPerArea;
                } else if (presetCore.skinMassPerArea > 0.0 ) {
                    part.skinMassPerArea = presetCore.skinMassPerArea;
                    tpsSurfaceDensity = 0.0f;
                } else {
                    part.skinMassPerArea = part.partInfo.partPrefab.skinMassPerArea;
                    tpsSurfaceDensity = 0.0f;
                }
                // skinThermalMassModifier
                if (presetTPS.skinSpecificHeatCapacity > 0.0) {
                    part.skinThermalMassModifier = presetTPS.skinSpecificHeatCapacity / PhysicsGlobals.StandardSpecificHeatCapacity / part.thermalMassModifier;
                } else if (presetCore.skinSpecificHeatCapacity > 0.0) {
                    part.skinThermalMassModifier = presetCore.skinSpecificHeatCapacity / PhysicsGlobals.StandardSpecificHeatCapacity / part.thermalMassModifier;
                } else {
                }
                // skinIntTransferCoefficient
                if (presetTPS.skinIntTransferCoefficient != double.PositiveInfinity | presetTPS.skinIntTransferCoefficient  > 0.0) {
                    skinIntTransferCoefficient = presetTPS.skinIntTransferCoefficient;
                    part.skinInternalConductionMult = skinIntTransferCoefficient * SkinInternalConductivityDiv / part.heatConductivity;
                } else if (presetCore.skinIntTransferCoefficient > 0.0 ) {
                    skinIntTransferCoefficient = presetCore.skinIntTransferCoefficient;
                    part.skinInternalConductionMult = skinIntTransferCoefficient * SkinInternalConductivityDiv / part.heatConductivity;
                } else {
                    part.skinInternalConductionMult = part.partInfo.partPrefab.skinInternalConductionMult;
                    skinIntTransferCoefficient = part.partInfo.partPrefab.skinInternalConductionMult / SkinInternalConductivityDiv * part.heatConductivity;
                }
            }
            part.skinSkinConductionMult = part.skinInternalConductionMult;


            // skinMaxTempOverride
            if (presetTPS.skinMaxTempOverride > 0) {
                part.skinMaxTemp = presetTPS.skinMaxTempOverride;
            } else if (presetCore.skinMaxTempOverride > 0 ) {
                part.skinMaxTemp = presetCore.skinMaxTempOverride;
            } else {
                part.skinMaxTemp = part.partInfo.partPrefab.skinMaxTemp;
            }
            // emissiveConstant
            if (presetTPS.emissiveConstantOverride > 0) {
                part.emissiveConstant = presetTPS.emissiveConstantOverride;
            } else if (presetCore.emissiveConstantOverride > 0 ) {
                part.emissiveConstant = presetCore.emissiveConstantOverride;
            } else {
                part.emissiveConstant = part.partInfo.partPrefab.emissiveConstant;
            }
            // absorptiveConstant
            if (presetTPS.absorptiveConstant > 0) {
                part.absorptiveConstant = presetTPS.absorptiveConstant;
            } else if (presetCore.absorptiveConstant > 0 ) {
                part.absorptiveConstant = presetCore.absorptiveConstant;
            } else {
                part.absorptiveConstant = absorptiveConstantOrig;
            }

            //prevent DRE from ruining everything
            if (DREHandler.Found && HighLogic.LoadedSceneIsFlight)
                DREHandler.SetOperationalTemps(part, part.maxTemp, part.skinMaxTemp);
        }

        public void UpdateGeometricProperties()
        {
            if (HighLogic.LoadedSceneIsEditor) {
                part.radiativeArea = SetSurfaceArea();
            }
            tpsMass = TPSMass;
            tpsCost = TPSCost;
            if (tpsMassIsAdditive) {
                if (moduleMass != tpsMass)
                {
                    //TODO Fire EngineersReport update without FAR voxelization
                    moduleMass = tpsMass;
                    EditorCordinator.ignoreNextShipModified = true;
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
                
            } else {
                moduleMass = 0.0f;
            }
            
            if ((modularPart != null || proceduralPart != null) && modAblator != null && modAblator.enabled)
            {
                if (ablatorResourceName != null)
                {
                    var ab = EnsureAblatorResource(ablatorResourceName);
                    double ratio = ab.maxAmount > 0 ? ab.amount / ab.maxAmount : 1.0;
                    ab.maxAmount = Ablator;
                    ab.amount = Math.Min(ratio * ab.maxAmount, ab.maxAmount);
                }

                if (outputResourceName != null)
                {
                    var ca = EnsureAblatorResource(outputResourceName);
                    ca.maxAmount = Ablator;
                    ca.amount = 0;
                }
            }
            part.UpdateMass();
            partMassCached = part.mass;
            //if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch?.ship != null)
                //GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartTweaked, part);
           
            UpdateGUI();

            // ModuleAblator's Start runs before this PM overrides the ablator values and will precalculate some parameters.
            // Run this precalculation again after we've finished configuring everything.
            if (HighLogic.LoadedSceneIsFlight)
                modAblator?.Start();
        }

        public void UpdateGUI() {
            part.GetResourceMass(out float resourceThermalMass);

            double mult = PhysicsGlobals.StandardSpecificHeatCapacity * part.thermalMassModifier;
            double thermalMass = partMassCached * (float)mult + resourceThermalMass;
            double skinThermalMass = (float)Math.Max(0.1, Math.Min(0.001 * part.skinMassPerArea * part.skinThermalMassModifier * surfaceArea * mult, (double)partMassCached * mult * 0.5));
            thermalMass = Math.Max(thermalMass - skinThermalMass, 0.1);
            thermalMassAreaBefore = part.skinMassPerArea * part.skinThermalMassModifier;
            //Debug.Log($"[ROThermal] UpdateGUI() skinThermalMass = " + skinThermalMass + "= 0.001 * part.skinMassPerArea: " + part.skinMassPerArea + " * part.skinThermalMassModifier: " + part.skinThermalMassModifier + " * surfaceArea: " + surfaceArea + " * mult: (" + PhysicsGlobals.StandardSpecificHeatCapacity + " * " + part.thermalMassModifier + ")");

            maxTempDisplay = "Skin: " + String.Format("{0:0.}", part.skinMaxTemp) + "K / Core: " + String.Format("{0:0.}", part.maxTemp) ;
            thermalMassDisplay = "Skin: " + FormatThermalMass(skinThermalMass) + " / Core: " + FormatThermalMass(thermalMass);
            //Debug.Log($"[ROThermal] UpdateGUI() thermalInsulance: skinIntTransferCoefficient " + skinIntTransferCoefficient + " presetCore.skinIntTransferCoefficient " + presetCore.skinIntTransferCoefficient );
            thermalInsulanceDisplay = KSPUtil.PrintSI(1.0/skinIntTransferCoefficient, "m²*K/kW", 4);
            emissiveConstantDisplay = part.emissiveConstant.ToString("F2");
            massDisplay = "Skin " + FormatMass((float)tpsMass) + " Total: " + FormatMass(partMassCached + resourceThermalMass);
            surfaceDensityDisplay = (float)tpsSurfaceDensity;

            FlightDisplay = "" + part.skinMaxTemp + "/" + part.maxTemp + "\nSkin: " + PresetTPS  + " " + tpsHeightDisplay + "mm\nCore: " + PresetCore ;
            //if (HighLogic.LoadedSceneIsEditor)
                //DebugLog();
            
            UpdatePAW();
        }

        public void CCTagUpdate(PresetROMatarial preset ) 
        {
            if (!(CCTagListModule is ModuleTagList))
                return;
            
            if(preset.name == "None")
            {
                if (reentryByDefault)
                    CCTagListModule.tags.AddUnique(reentryTag);
                else if (!reentryByDefault & CCTagListModule.tags.Contains(reentryTag))
                    CCTagListModule.tags.Remove(reentryTag);

                CCTagListModule.tags.Sort();
            }
            else if(preset.reentryTag == reentryTag &  !CCTagListModule.tags.Contains(reentryTag))
            {
                CCTagListModule.tags.Add(preset.reentryTag);
                CCTagListModule.tags.Sort();

            }
            else if(preset.reentryTag != reentryTag &  CCTagListModule.tags.Contains(reentryTag))
            {
                CCTagListModule.tags.Remove(preset.reentryTag);
                CCTagListModule.tags.Sort();

            }

        }

        public void DebugLog()
        {
            //part.DragCubes.RequestOcclusionUpdate();
            //part.DragCubes.SetPartOcclusion();
            double skinThermalMassModifier;
            if (presetTPS.skinHeightMax > 0.0 & presetTPS.skinSpecificHeatCapacityMax > 0.0 & presetTPS.skinMassPerAreaMax > 0.0) 
            {
                double heightfactor = (tpsHeightDisplay / 1000 - presetTPS.skinHeightMin) / (presetTPS.skinHeightMax - presetTPS.skinHeightMin);
                skinThermalMassModifier = (presetTPS.skinSpecificHeatCapacityMax - presetTPS.skinSpecificHeatCapacity) * heightfactor + presetTPS.skinSpecificHeatCapacity
                                                    / PhysicsGlobals.StandardSpecificHeatCapacity / part.thermalMassModifier;
            } else {
               skinThermalMassModifier = presetTPS.skinSpecificHeatCapacity > 0.0 ? presetTPS.skinSpecificHeatCapacity : presetCore.skinSpecificHeatCapacity;
            }
            skinThermalMassModifier /= PhysicsGlobals.StandardSpecificHeatCapacity / part.thermalMassModifier;
            part.GetResourceMass(out float resourceThermalMass);
            double mult = PhysicsGlobals.StandardSpecificHeatCapacity * part.thermalMassModifier;
            float thermalMass = part.mass * (float)mult + resourceThermalMass;
            float skinThermalMass = (float)Math.Max(0.1, Math.Min(0.001 * part.skinMassPerArea * part.skinThermalMassModifier * surfaceArea * mult, (double)part.mass * mult * 0.5));
            thermalMass = Mathf.Max(thermalMass - skinThermalMass, 0.1f);

            Debug.Log($"[ROThermal] (" + HighLogic.LoadedScene + ") Values for " + part.name + "\n"
                    + "Core Preset: " + presetCore.name + ", Skin Preset: " +  presetTPS.name + ": " + tpsHeightDisplay + " mm\n"
                    + "TempMax: Skin: " + part.skinMaxTemp + "K / Core: " + part.maxTemp + "K\n"
                    + "ThermalMassMod Part Skin: " + part.skinThermalMassModifier + ", Core: "  + part.thermalMassModifier + "\n"
                    + "             Module Skin: " + skinThermalMassModifier + ", Core: "  + presetCore.specificHeatCapacity + "\n"
                    + "skinMassPerArea Part" + part.skinMassPerArea + ", Module " + tpsSurfaceDensity + "\n"
                    + "ConductionMult Part: Internal " + part.skinInternalConductionMult + ", SkintoSkin " + part.skinSkinConductionMult + ", Conductivity " + part.heatConductivity + "\n"
                    + "             Module: Internal " + skinIntTransferCoefficient * SkinInternalConductivityDiv / part.heatConductivity + ", Skin to Skin " 
                                        + presetTPS.skinSkinConductivity + ", Conductivity " + presetCore.thermalConductivity + "\n"
                    + "emissiveConstant part " + part.emissiveConstant + ",    preset" + presetTPS.emissiveConstantOverride + "\n"
                    + "ThermalMass Part: Skin: " + FormatThermalMass((float)part.skinThermalMass) + " / Core: " + FormatThermalMass((float)part.thermalMass) + "\n"
                    + "          Module: Skin: " + FormatThermalMass(skinThermalMass) + " / Core: " + FormatThermalMass(thermalMass) + "\n"
                    + "ModuleMass (Skin) " + FormatMass(moduleMass) + ",    Total Mass: " + FormatMass(part.mass) + "\n"
                    + "SurfaceArea: part.radiativeArea " + part.radiativeArea + ", get_SurfaceArea" + surfaceArea + "far" + fARAeroPartModule?.ProjectedAreas.totalArea + "\n"
                    //+ "SurfaceArea: part.exposedArea " + part.exposedArea + ", part.skinExposedArea "  + part.skinExposedArea + ", skinExposedAreaFrac " + part.skinExposedAreaFrac + "\n"
                    + "part.DragCubes->  PostOcclusionArea " + part.DragCubes.PostOcclusionArea  + ", cubeData.exposedArea "+ part.DragCubes.ExposedArea + ", Area "+ part.DragCubes.Area + "\n"
            );
        }

        public void UpdateCoreForRealfuels(bool applyPreset)
        {
            List<string> availableMaterialsNamesForFuelTank = new List<string>();
            string logStr = "";
            foreach (string name in availablePresetNamesCore) {
                if (PresetROMatarial.PresetsCore.TryGetValue(name, out PresetROMatarial preset)) {
                    if (preset.restrictors.Contains(moduleFuelTanks.type)){
                    availableMaterialsNamesForFuelTank.Add(name);
                    logStr += name + ", ";
                    }
                }
                else {
                    Debug.LogWarning($"[ROThermal] preset \"{name}\" in corePresets not found");
                }
                
            }
            if (availableMaterialsNamesForFuelTank.Any()) {
                presetCoreName = availableMaterialsNamesForFuelTank[0];
                string[] strList = availableMaterialsNamesForFuelTank.ToArray();
                UpdatePresetsList(strList, PresetType.Core);
                Debug.Log($"[ROThermal] UpdateFuelTankCore() " + moduleFuelTanks.type + " found in " + logStr 
                            + "\n\r presetCoreName set as " + availableMaterialsNamesForFuelTank[0]);
                if (applyPreset)
                    ApplyCorePreset(presetCoreName);
            } else {
                Debug.Log("[ROThermal] No fitting PresetROMatarial for " + moduleFuelTanks.type + " found in " + part.name);   
            }
        }

        public string[] GetUnlockedPresets(string[] all)
        {
            if (HighLogic.CurrentGame.Mode != Game.Modes.CAREER &&
                HighLogic.CurrentGame.Mode != Game.Modes.SCIENCE_SANDBOX)
            {
                Debug.Log($"[ROThermal] All presets unlocked");
                return all;
            }

            var unlocked = new List<string>();
            foreach (string s in all)
            {
                if (IsConfigUnlocked(s))
                {
                    unlocked.AddUnique(s);
                }
            }
            Debug.Log($"[ROThermal] presets {unlocked} are unlocked");

            return unlocked.ToArray();
        }

        public bool IsConfigUnlocked(string configName)
        {
            if (!PartUpgradeManager.Handler.CanHaveUpgrades()) return true;

            PartUpgradeHandler.Upgrade upgd = PartUpgradeManager.Handler.GetUpgrade(configName);
            if (upgd == null) return true;

            if (PartUpgradeManager.Handler.IsEnabled(configName)) return true;

            if (upgd.entryCost < 1.1 && PartUpgradeManager.Handler.IsAvailableToUnlock(configName) &&
                PurchaseConfig(upgd))
            {
                return true;
            }

            return false;
        }

        public bool PurchaseConfig(PartUpgradeHandler.Upgrade upgd)
        {
            if (Funding.CanAfford(upgd.entryCost))
            {
                PartUpgradeManager.Handler.SetUnlocked(upgd.name, true);
                GameEvents.OnPartUpgradePurchased.Fire(upgd);
                return true;
            }

            return false;
        }

        private void UpdatePresetsList(string[] presetNames, PresetType type)
        {
            BaseField bf;

            if (type == PresetType.Core){
                bf = Fields[nameof(presetCoreName)];
                if (!Fields[nameof(presetCoreName)].guiActiveEditor) {
                    return;
                }               
            } else {
                bf = Fields[nameof(presetSkinName)];
            }

            var dispValues = RP1Found && HighLogic.LoadedScene != GameScenes.LOADING ?
                presetNames.Select(p => ConstructColoredPresetTitle(p)).ToArray() : presetNames;
            if (presetNames.Length == 0)
            {
                presetNames = dispValues = new string[] { "NONE" };
            }
            var uiControlEditor = bf.uiControlEditor as UI_ChooseOption;
            uiControlEditor.options = presetNames;
            uiControlEditor.display = dispValues;  
        }

        private string ConstructColoredPresetTitle(string presetName)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                return presetName;

            string partTech = part.partInfo.TechRequired;
            if (string.IsNullOrEmpty(partTech) || ResearchAndDevelopment.GetTechnologyState(partTech) != RDTech.State.Available)
                return $"<color=orange>{presetName}</color>";

            PartUpgradeHandler.Upgrade upgrade = PartUpgradeManager.Handler.GetUpgrade(presetName);
            bool isTechAvailable = upgrade == null || ResearchAndDevelopment.GetTechnologyState(upgrade.techRequired) == RDTech.State.Available;
            return isTechAvailable ? presetName : $"<color=orange>{presetName}</color>";
        }

        private void EnsureBestAvailableConfigSelected()
        {
            if (IsConfigUnlocked(presetCoreName)) return;

            string bestConfigMatch = null;
            for (int i = availablePresetNamesCore.IndexOf(presetCoreName) - 1; i >= 0; i--)
            {
                bestConfigMatch = availablePresetNamesCore[i];
                if (IsConfigUnlocked(bestConfigMatch)) break;
            }

            if (bestConfigMatch != null)
            {
                PresetCore = bestConfigMatch;
                ApplyCorePreset(presetCoreName);
            }
        }

        public void UpdatePAW()
        {
            foreach (UIPartActionWindow window in UIPartActionController.Instance.windows)
            {
                if (window.part == this.part)
                {
                    window.displayDirty = true;
                }
            }
        }

        private void RemoveAblatorResources()
        {
            if (ablatorResourceName != null)
            {
                part.Resources.Remove(ablatorResourceName);
            }

            if (outputResourceName != null)
            {
                part.Resources.Remove(outputResourceName);
            }
        }

        private PartResource EnsureAblatorResource(string name)
        {
            PartResource res = part.Resources[name];
            if (res == null)
            {
                PartResourceDefinition resDef = PartResourceLibrary.Instance.GetDefinition(name);
                if (resDef == null)
                {
                    Debug.LogError($"[ROThermal] Resource {name} not found!");
                    return null;
                }

                res = new PartResource(part);
                res.resourceName = name;
                res.SetInfo(resDef);
                res._flowState = true;
                res.isTweakable = resDef.isTweakable;
                res.isVisible = resDef.isVisible;
                res.hideFlow = false;
                res._flowMode = PartResource.FlowMode.Both;
                part.Resources.dict.Add(resDef.id, res);
            }

            return res;
        }

        void ensurePresetIsInList (ref string [] list, string preset) {
            if(!list.Contains(preset))
            {
                int i = list.Length;
                Array.Resize(ref list, i + 1);
                list[i] = preset;
            }
        }

        /// <summary>
        /// Called from RP0KCT
        /// </summary>
        /// <param name="validationError"></param>
        /// <param name="canBeResolved"></param>
        /// <param name="costToResolve"></param>
        /// <returns></returns>
        public virtual bool Validate(out string validationError, out bool canBeResolved, out float costToResolve, out string techToResolve)
        {
            validationError = null;
            canBeResolved = false;
            costToResolve = 0;
            techToResolve = null;

            if (IsConfigUnlocked(presetCoreName)) return true;

            PartUpgradeHandler.Upgrade upgd = PartUpgradeManager.Handler.GetUpgrade(presetCoreName);
            if (upgd != null)
                techToResolve = upgd.techRequired;
            if (PartUpgradeManager.Handler.IsAvailableToUnlock(presetCoreName))
            {
                canBeResolved = true;
                costToResolve = upgd.entryCost;
                validationError = $"purchase config {upgd.title}";
            }
            else
            {
                validationError = $"unlock tech {ResearchAndDevelopment.GetTechnologyTitle(upgd.techRequired)}";
            }

            return false;
        }

        /// <summary>
        /// Called from RP0KCT
        /// </summary>
        /// <returns></returns>
        public virtual bool ResolveValidationError()
        {
            PartUpgradeHandler.Upgrade upgd = PartUpgradeManager.Handler.GetUpgrade(presetCoreName);
            if (upgd == null) return false;

            return PurchaseConfig(upgd);
        }
        public static string FormatMass(float mass) => mass < 1.0f ? KSPUtil.PrintSI(mass * 1e6, "g", 4) : KSPUtil.PrintSI(mass, "t", 4);
        public static string FormatThermalMass(float thermalmass) => KSPUtil.PrintSI(thermalmass * 1e3, "J/K", 4);
        public static string FormatThermalMass(double thermalmass) => KSPUtil.PrintSI(thermalmass * 1e3, "J/K", 4);

        #endregion Custom Methods
    }
}