using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSPShaderTools;
using ROUtils;

namespace ROLib
{
    public static class ROLModInterop
    {
        private static bool initialized = false;
        private static Assembly FARAssembly, RFAssembly, MFTAssembly, SolverEnginesAssembly;
        private static MethodInfo MFTChangeTotalVolumeMI, MFTCalculateMassMI;
        private static PropertyInfo SolverEnginesTempPI;
        private static Type MFTType, SolverEngineType;

        public static void UpdateResourceVolume(Part part)
        {
            float totalVolume = 0;
            foreach (IContainerVolumeContributor contrib in part.FindModulesImplementing<IContainerVolumeContributor>())
            {
                foreach (ContainerContribution c in contrib.getContainerContributions())
                {
                    totalVolume += c.containerVolume;
                }
            }
            RealFuelsVolumeUpdate(part, totalVolume);
        }

        /// <summary>
        /// Updates part highlight renderer list, sends message to ModuleROTFlagDecal to update its renderer,
        ///  sends message to FAR to update voxels
        /// Should be called anytime the model geometry in a part is changed -- either models added/deleted, procedural meshes updated.  Other methods exist for pure drag-cube updating in ROLStockInterop.
        /// </summary>
        /// <param name="part"></param>
        /// <param name="shapeKey">Key that uniquely idenfies the geometry of the part. Used for drag cube caching.</param>
        public static void OnPartGeometryUpdate(Part part, string shapeKey = null)
        {
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight) { return; }//noop on prefabs
            ROLStockInterop.UpdatePartHighlighting(part);
            part.airlock = LocateAirlock(part);
            PartGeometryUpdate(part);
            DragCubeTool.UpdateDragCubes(part, shapeKey);
            if (HighLogic.LoadedSceneIsEditor && part.parent == null && part != EditorLogic.RootPart) //likely the part under the cursor; this fixes problems with modular parts not wanting to attach to stuff
            {
                part.gameObject.SetLayerRecursive(1, 2097152);//1<<21 = Part Triggers get skipped by the relayering (hatches, ladders, ??)
            }
        }

        public static void OnPartTextureUpdated(Part part)
        {
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
                TextureCallbacks.onTextureSetChanged(part);
        }

        private static void PartGeometryUpdate(Part part)
        {
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
                TextureCallbacks.onPartModelChanged(part);
        }

        private static Transform LocateAirlock(Part part) =>
            part.GetComponentsInChildren<Collider>().FirstOrDefault(x => x.gameObject.tag == "Airlock")?.transform;

        public static bool RealFuelsVolumeUpdate(Part part, float liters)
        {
            if (!IsRFInstalled() && !IsMFTInstalled())
            {
                ROLLog.error($"Config for {part} is set to use RF/MFT, but neither RF nor MFT is installed, cannot update part volumes through them.  Please check your configs and/or patches for errors.");
                return false;
            }

            if (MFTChangeTotalVolumeMI is MethodInfo && MFTCalculateMassMI is MethodInfo && GetModuleFuelTanks(part) is PartModule pm)
            {
                double volumeLiters = liters;
                MFTChangeTotalVolumeMI.Invoke(pm, new object[] { volumeLiters, false });
                MFTCalculateMassMI.Invoke(pm, new object[] { });
                UpdatePartResourceDisplay(part);
                // ROLLog.debug($"ROTModInterop - Set RF/MFT total tank volume to: {volumeLiters} Liters for part: {part.name}");
                return true;
            }
            else
            {
                ROLLog.error($"Could not find ModuleFuelTank in part {part} for RealFuels/MFT!");
                return false;
            }
        }

        public static PartModule GetModuleFuelTanks(Part part) =>
            ((IsRFInstalled() || IsMFTInstalled()) && MFTType is Type) ? part.GetComponent(MFTType) as PartModule : null;

        public static bool HasModuleFuelTanks(Part part) => GetModuleFuelTanks(part) != null;

        public static bool hasModuleEngineConfigs(Part part) =>
            Type.GetType("RealFuels.ModuleEngineConfigs,RealFuels") is Type type && part.GetComponent(type) is PartModule;

        public static PropertyInfo getSolverEngineTempProperty()
        {
            if (!IsSolverEnginesInstalled()) return null;
            return SolverEnginesTempPI;
        }

        public static ModuleEngines getSolverEngineModule(Part part, string engineID)
        {
            ModuleEngines engine = null;
            if (IsSolverEnginesInstalled() && SolverEngineType is Type)
            {
                engine = Array.ConvertAll(part.GetComponents(SolverEngineType), x => (ModuleEngines)x).FirstOrDefault(x => x.engineID == engineID);
                engine ??= part.GetComponent(SolverEngineType) as ModuleEngines;
            }
            return engine;
        }

        private static void Init()
        {
            initialized = true;
            FARAssembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "FerramAerospaceResearch")?.assembly;
            RFAssembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "RealFuels")?.assembly;
            MFTAssembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "modularFuelTanks")?.assembly;
            SolverEnginesAssembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "SolverEngines")?.assembly;
            if (RFAssembly is Assembly || MFTAssembly is Assembly)
            {
                string targ = RFAssembly is Assembly ? "RealFuels.Tanks.ModuleFuelTanks,RealFuels" : "RealFuels.Tanks.ModuleFuelTanks,modularFuelTanks";
                MFTType = Type.GetType(targ);
                MFTChangeTotalVolumeMI = MFTType?.GetMethod("ChangeTotalVolume");
                MFTCalculateMassMI = MFTType?.GetMethod("CalculateMass");
            }
            if (SolverEnginesAssembly is Assembly)
            {
                SolverEngineType = Type.GetType("SolverEngines.ModuleEnginesSolver,SolverEngines");
                SolverEnginesTempPI = SolverEngineType?.GetProperty("GetEngineTemp");
            }
        }

        public static bool IsFARInstalled()
        {
            if (!initialized) Init();
            return FARAssembly is Assembly;
        }

        public static bool IsRFInstalled()
        {
            if (!initialized) Init();
            return RFAssembly is Assembly;
        }

        public static bool IsMFTInstalled()
        {
            if (!initialized) Init();
            return MFTAssembly is Assembly;
        }

        public static bool IsSolverEnginesInstalled()
        {
            if (!initialized) Init();
            return SolverEnginesAssembly is Assembly;
        }

        public static void UpdatePartResourceDisplay(Part part)
        {
            if (HighLogic.LoadedSceneIsEditor && part.PartActionWindow is UIPartActionWindow)
            {
                foreach (UIPartActionResourceEditor pare in part.PartActionWindow?.ListItems?.Where(x => x is UIPartActionResourceEditor))
                {
                    pare.resourceMax.text = KSPUtil.LocalizeNumber(pare.Resource.maxAmount, "F1");
                    pare.UpdateItem();
                    pare.slider.onValueChanged.Invoke(pare.slider.value);
                }
            }
        }
    }
}
