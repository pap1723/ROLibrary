using FerramAerospaceResearch.FARAeroComponents;
using FerramAerospaceResearch.FARGUI.FAREditorGUI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace ROLib
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorCordinator : MonoBehaviour
    {
        public static EditorCordinator Instance { get; private set; }
        private static EditorGUI farEditorGUI;
        private static List<ModuleROMaterials> massUpdateCheckList = new List<ModuleROMaterials>();
        private static bool farVoxelUpdateQueLast = false;
        private static int waitForFAR = 0;
        private static int tryCount = 0;
        private const int tries = 100; 
        private static int recheckMassUpdateTry = tries;
        public static bool ignoreNextShipModified = false;
        private static bool massUpdateCheck = false;
        

        public void Start()
        {
            if (Instance == null)
            {
                GameEvents.onEditorShipModified.Add(OnEditorShipModified);
                Instance = this;
                Debug.Log($"[ROLib EditorCordinator] Main Instance created");
            }
            else
            {
                Debug.Log($"[ROLib EditorCordinator] Instance created & Destroyed");
                Destroy(this);
                return;
            }
            bool found = false;
            farEditorGUI = EditorGUI.Instance;
            if (farEditorGUI != null)
                found = true;

            Debug.Log($"[ROLib EditorCordinator] Looking for EditorGUI.Instance, found {found}");
            farVoxelUpdateQueLast = farEditorGUI.VoxelizationUpdateQueued;
        }
        private void OnDestroy() {
            if (Instance = this)
            {
                GameEvents.onEditorShipModified.Remove(OnEditorShipModified);
                massUpdateCheckList.Clear();
                Debug.Log($"[ROLib EditorCordinator] Main Instance destroyed");
            }
            else
            {
                Debug.Log($"[ROLib EditorCordinator] non Instance destroyed");
            }  
        }

        public void StartFinished() 
        {
            bool found = false;
            farEditorGUI = EditorGUI.Instance;
            if (farEditorGUI != null)
                found = true;

            Debug.Log($"[ROLib EditorCordinator] Looking for EditorGUI.Instance, found {found}");
            farVoxelUpdateQueLast = farEditorGUI.VoxelizationUpdateQueued;
        }

        
        private void FixedUpdate()
        {
            if (EditorLogic.RootPart != null)
            {
                if (massUpdateCheck & recheckMassUpdateTry > 0)
                {
                    recheckMassUpdateTry --;
                    foreach (ModuleROMaterials module in massUpdateCheckList) 
                    {
                        if (module == null)
                        {
                            RemoveToMassCheckList(module);
                            break;
                        }                            
                        module?.checkMassUpdate(true, tries - recheckMassUpdateTry);
                    }
                }


                // Workaround to check voxelization status of Vessel in Editor
                // Checks if the farEditorGUI changes the VoxelizationUpdateQueued Variable, subsequently updating all Vessel voxel and therefore the Surface Area.
                if (farEditorGUI != null)
                {
                    // wait for timer before checking voxelization completion again
                    if (waitForFAR > 0)
                    {
                        waitForFAR --;
                    }
                    else if (farVoxelUpdateQueLast == true & farEditorGUI.VoxelizationUpdateQueued == false | waitForFAR == 0)
                    {

                        foreach (Part part in EditorLogic.SortedShipList)
                        {
                            if (part?.HasModuleImplementing<FARAeroPartModule>() == true 
                                && part?.FindModuleImplementing<ModuleROMaterials>() is ModuleROMaterials b)
                            {
                                // compere change in ProjectedAreas.totalArea
                                if(!b.TrySurfaceAreaUpdate())
                                {  
                                    waitForFAR = 10;
                                    tryCount += 10;
                                    //EditorGUI.RequestUpdateVoxel();
                                }
                            }
                        }
                        if (waitForFAR == 0 | tryCount >= 410)
                        {
                            waitForFAR = -1;
                            tryCount = 0;
                        }
                        farVoxelUpdateQueLast = farEditorGUI.VoxelizationUpdateQueued;
                    }
                    else if (farVoxelUpdateQueLast != farEditorGUI.VoxelizationUpdateQueued)
                        farVoxelUpdateQueLast = farEditorGUI.VoxelizationUpdateQueued;
                }
                else
                {
                    Debug.Log($"[ROLib EditorCordinator] failed if check (farEditorGUI != null)");
                    farEditorGUI = EditorGUI.Instance;
                    farVoxelUpdateQueLast = farEditorGUI.VoxelizationUpdateQueued;
                }
            }
        }

        public static void AddToMassCheckList(ModuleROMaterials module){
            Debug.Log($"[ROLib EditorCordinator] massCheckList AddUnique {module.part}");
            massUpdateCheckList.AddUnique(module);
            massUpdateCheck = true;
        }
        public static void RemoveToMassCheckList(ModuleROMaterials module) 
        {
            if (massUpdateCheckList.Contains(module))
            {   
                massUpdateCheckList.Remove(module);
                Debug.Log($"[ROLib EditorCordinator] massCheckList Remove {module.part}");
            }
            if (!massUpdateCheckList.Any())
                massUpdateCheck = false;
        }  

        public void OnEditorShipModified(ShipConstruct ship)
        {
            Debug.Log($"[ROLib EditorCordinator] ShipModified");
            if (ignoreNextShipModified)
            {
                ignoreNextShipModified = false;
                return;
            }
                
            Debug.Log($"[ROLib EditorCordinator] ShipModified setting recheckMassWait");
            recheckMassUpdateTry = tries;
        }
    }
}