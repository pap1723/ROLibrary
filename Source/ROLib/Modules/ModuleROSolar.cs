using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ROLib
{
    public class ModuleROSolar : ModuleDeployableSolarPanel, IPartCostModifier, IPartMassModifier
    {
        public const string GroupName = "ROSolarGroup";
        public const string GroupDisplayName = "RO-Solar";

        #region KSPFields

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Variant", groupName = GroupName, groupDisplayName = GroupDisplayName),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentVariant = "Default";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Design", groupName = GroupName, groupDisplayName = GroupDisplayName),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentCore = "Mount-None";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Panel Length", guiFormat = "N3", guiUnits = "m", groupName = GroupName),
         UI_FloatEdit(sigFigs = 4, suppressEditorShipModified = true)]
        public float panelLength = 1.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Panel Width", guiFormat = "N3", guiUnits = "m", groupName = GroupName),
         UI_FloatEdit(sigFigs = 4, suppressEditorShipModified = true)]
        public float panelWidth = 1.0f;

        [KSPField(isPersistant = true, guiName = "Panel Scale", guiFormat = "N3", guiUnits = "x", groupName = GroupName),
         UI_FloatEdit(sigFigs = 4, suppressEditorShipModified = true)]
        public float panelScale = 1.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Tech Level", guiFormat = "N0", groupName = GroupName),
        UI_FloatRange(minValue = 0f, stepIncrement = 1f, scene = UI_Scene.Editor, suppressEditorShipModified = true)]
        public float TechLevel = -1f;
        public int techLevel => Convert.ToInt32(TechLevel);

        [KSPField(guiName = "Tech Level", guiFormat = "N0", groupName = GroupName)]
        public int tlText = 0;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Charge Rate (1AU)", guiFormat = "F4", guiUnits = " kW", groupName = GroupName, groupDisplayName = GroupDisplayName)]
        public float currentRate = 0.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = true, guiName = "Area", guiFormat = "F4", guiUnits = " m^2", groupName = GroupName)]
        public float area = 0.0f;

        [KSPField(guiActiveEditor = true, guiName = "Mass", guiFormat = "F4", guiUnits = " t", groupName = GroupName)]
        public float mass = 0.0f;

        [KSPField(guiActiveEditor = true, guiName = "Cost", guiFormat = "F1", groupName = GroupName)]
        public float cost = 0.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Tracking", groupName = GroupName),
        UI_Toggle(scene = UI_Scene.Editor)]
        public bool trackingToggle = false;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Debug Suncatching", groupName = GroupName),
        UI_Toggle(scene = UI_Scene.All)]
        public bool drawDebugRays = false;

        [KSPEvent(guiActiveEditor = true, guiName = "Reset Model to Original", groupName = GroupName)]
        public void ResetModelEvent()
        {
            ResetModel();
            foreach (Part p in part.symmetryCounterparts)
            {
                if (p.FindModuleImplementing<ModuleROSolar>() is ModuleROSolar m)
                    m.ResetModel();
            }
        }

        private void ResetModel()
        {
            panelLength = coreModule.definition.panelLength;
            panelWidth = coreModule.definition.panelWidth;
            panelScale = coreModule.definition.panelScale;
            SetUIVisibleFields();
            ModelChangedHandler(true);
            prevLength = panelLength;
            prevWidth = panelWidth;
            prevScale = panelScale;
            MonoUtilities.RefreshPartContextWindow(part);
        }

        [KSPField] public string solarPanelType = "static";
        [KSPField] public float largeStep = 1.0f;
        [KSPField] public float smallStep = 0.1f;
        [KSPField] public float slideStep = 0.001f;
        [KSPField] public float minWidth = 0.1f;
        [KSPField] public float maxWidth = 100.0f;
        [KSPField] public float minLength = 0.1f;
        [KSPField] public float maxLength = 100.0f;

        [KSPField] public string solarCore = "Mount-None";
        [KSPField] public float addMass = 0.0f;
        [KSPField] public float addCost = 0.0f;
        [KSPField] public string coreManagedNodes = string.Empty;
        [KSPField] public int maxTechLevel = 0;

        #endregion KSPFields

        #region Custom Fields

        ModelDefinitionLayoutOptions[] coreDefs;
        private SolarTechLimit stl;

        // Previous length/width/scale values for change detection
        private float prevLength = -1;
        private float prevWidth = -1;
        private float prevScale = -1;

        private LineRenderer trackingRenderer, sunDirRenderer, panelRotRenderer;
        private GameObject trackingDrawer, sunDirDrawer, panelOrientationDrawer;
        private static Material lineMaterial;

        private bool lengthWidth { get => coreModule?.definition.lengthWidth ?? false; }

        /// <summary>
        /// Standard work-around for lack of config-node data being passed consistently and lack of support for mod-added serializable classes.
        /// </summary>
        [Persistent]
        public string configNodeData = string.Empty;

        /// <summary>
        /// Has initialization been run?  Set to true the first time init methods are run (OnLoad/OnStart), and ensures that init is only run a single time.
        /// </summary>
        private bool initialized = false;

        private string[] coreNodeNames;
        private ROLModelModule<ModuleROSolar> coreModule;

        /// <summary>
        /// Mapping of all of the variant sets available for this part.  When variant list length > 0, an additional 'variant' UI slider is added to allow for switching between variants.
        /// </summary>
        private readonly Dictionary<string, ModelDefinitionVariantSet> variantSets = new Dictionary<string, ModelDefinitionVariantSet>();

        /// <summary>
        /// Helper method to get or create a variant set for the input variant name.  If no set currently exists, a new set is empty set is created and returned.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private ModelDefinitionVariantSet GetVariantSet(string name)
        {
            if (!variantSets.TryGetValue(name, out ModelDefinitionVariantSet set))
            {
                set = new ModelDefinitionVariantSet(name);
                variantSets.Add(name, set);
            }
            return set;
        }

        #endregion Custom Fields

        #region Standard KSP Overrides

        public override void OnLoad(ConfigNode node)
        {
            if (node.name != "CURRENTUPGRADE")
            {
                if (string.IsNullOrEmpty(configNodeData))
                    configNodeData = node.ToString();
                Initialize();
            }
            base.OnLoad(node);
            stl ??= SolarTechLimit.GetTechLevel(techLevel);
            ReloadTimeCurve();  //OnStart() appears too late for setting the TimeEfficCurve for Kerbalism's SolarPanelFixer.
        }

        public override void OnStart(StartState state)
        {
            SetMaxTechLevel();
            Initialize();
            ModelChangedHandler(false);
            base.OnStart(state);
            InitializeUI();
        }

        public void Update()
        {
            DrawRays();
        }

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;
        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => Mathf.Max(mass, 0.0001f);
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => Mathf.Max(cost, 0);

        #endregion Standard KSP Overrides

        #region Custom Update Methods

        /// <summary>
        /// Initialization method.  Sets up model modules, loads their configs from the input config node.  Does all initial linking of part-modules.<para/>
        /// Does NOT set up their UI interaction -- that is all handled during OnStart()
        /// </summary>
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            stl ??= SolarTechLimit.GetTechLevel(techLevel);

            prevLength = panelLength;
            prevWidth = panelWidth;
            prevScale = panelScale;

            coreNodeNames = ROLUtils.parseCSV(coreManagedNodes);

            // Model-Module Setup / Initialization
            ConfigNode node = ROLConfigNodeUtils.ParseConfigNode(configNodeData);

            // List of CORE model nodes from config
            // each one may contain multiple 'model=modelDefinitionName' entries
            // but must contain no more than a single 'variant' entry.
            // If no variant is specified, they are added to the 'Default' variant.
            ConfigNode[] coreDefNodes = node.GetNodes("CORE");

            List<ModelDefinitionLayoutOptions> coreDefList = new List<ModelDefinitionLayoutOptions>();
            foreach (ConfigNode cn in coreDefNodes)
            {
                string variantName = cn.ROLGetStringValue("variant", "Default");
                coreDefs = ROLModelData.getModelDefinitionLayouts(cn.ROLGetStringValues("model"));
                coreDefList.AddUniqueRange(coreDefs);
                ModelDefinitionVariantSet mdvs = GetVariantSet(variantName);
                mdvs.AddModels(coreDefs);
            }
            coreDefs = coreDefList.ToArray();

            coreModule = new ROLModelModule<ModuleROSolar>(part, this, ROLUtils.GetRootTransform(part, "ModuleROSolar-CORE"), ModelOrientation.CENTRAL, nameof(currentCore), null, null, null);
            coreModule.name = "ModuleROSolar-Core";
            coreModule.getSymmetryModule = m => m.coreModule;
            coreModule.getValidOptions = () => GetVariantSet(currentVariant).definitions;

            coreModule.setupModelList(coreDefs);
            coreModule.setupModel(true);

            CreateLineMaterial();
            UpdateModulePositions();
            UpdateAnimationAndTracking();
        }

        internal void ModelChangedHandler(bool pushNodes)
        {
            stl = SolarTechLimit.GetTechLevel(techLevel);
            retractable = stl.retractable && solarPanelType != "static";
            useRaycastForTrackingDot = true;
            UpdateModulePositions();
            UpdateAnimationAndTracking();
            startFSM();
            UpdateAttachNodes(pushNodes);
            UpdateAvailableVariants();
            UpdateDragCubes();
            UpdateMassAndCost();
            RecalculateStats();
            ROLStockInterop.UpdatePartHighlighting(part);
            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields[nameof(trackingToggle)].guiActiveEditor = stl.isTracking && coreModule.definition.isTracking && solarPanelType != "static";
                ROLStockInterop.FireEditorUpdate();
            }
            RemakeTrackingPointers();
        }

        /// <summary>
        /// Initialize the UI controls, including default values, and specifying delegates for their 'onClick' methods.<para/>
        /// All UI based interaction code will be defined/run through these delegates.
        /// </summary>
        public void InitializeUI()
        {
            // Set up the core variant UI control
            string[] variantNames = ROLUtils.getNames(variantSets.Values, m => m.variantName);
            this.ROLupdateUIChooseOptionControl(nameof(currentVariant), variantNames, variantNames);
            Fields[nameof(currentVariant)].guiActiveEditor = variantSets.Count > 1;
            Fields[nameof(currentVariant)].uiControlEditor.onFieldChanged = (a, b) =>
            {
                // Query the index from that variant set
                ModelDefinitionVariantSet prevMdvs = GetVariantSet(coreModule.definition.name);
                // This is the index of the currently selected model within its variant set
                int previousIndex = prevMdvs.IndexOf(coreModule.layoutOptions);
                // Grab ref to the current/new variant set
                ModelDefinitionVariantSet mdvs = GetVariantSet(currentVariant);
                // And a reference to the model from same index out of the new set ([] call does validation internally for IAOOBE)
                ModelDefinitionLayoutOptions newCoreDef = mdvs[previousIndex];
                // Now, call model-selected on the core model to update for the changes, including symmetry counterpart updating.
                this.ROLactionWithSymmetry(m =>
                {
                    m.coreModule.modelSelected(newCoreDef.definition.name, false);
                    m.ResetModel();
                });
            };

            Fields[nameof(currentCore)].uiControlEditor.onFieldChanged =
            Fields[nameof(currentCore)].uiControlEditor.onSymmetryFieldChanged = OnModelSelectionChanged;

            Fields[nameof(panelLength)].uiControlEditor.onFieldChanged =
            Fields[nameof(panelLength)].uiControlEditor.onSymmetryFieldChanged = (a, b) =>
            {
                if ((float) a.GetValue(this) != prevLength)
                {
                    ModelChangedHandler(true);
                    prevLength = panelLength;
                }
            };

            Fields[nameof(panelWidth)].uiControlEditor.onFieldChanged =
            Fields[nameof(panelWidth)].uiControlEditor.onSymmetryFieldChanged = (a, b) =>
            {
                if ((float) a.GetValue(this) != prevWidth)
                {
                    ModelChangedHandler(true);
                    prevWidth = panelWidth;
                }
            };

            Fields[nameof(panelScale)].uiControlEditor.onFieldChanged =
            Fields[nameof(panelScale)].uiControlEditor.onSymmetryFieldChanged = (a, b) =>
            {
                if ((float)a.GetValue(this) != prevScale)
                {
                    ModelChangedHandler(true);
                    prevScale = panelScale;
                }
            };

            Fields[nameof(TechLevel)].uiControlEditor.onFieldChanged =
            Fields[nameof(TechLevel)].uiControlEditor.onSymmetryFieldChanged = (a, b) =>
            {
                ModelChangedHandler(true);
            };

            Fields[nameof(trackingToggle)].uiControlEditor.onFieldChanged =
            Fields[nameof(trackingToggle)].uiControlEditor.onSymmetryFieldChanged = (a, b) =>
            {
                ModelChangedHandler(true);
            };

            SetUIVisibleFields();
        }

        private void OnModelSelectionChanged(BaseField f, object o)
        {
            if (f.name == Fields[nameof(currentCore)].name) coreModule.modelSelected(currentCore, false);
            SetUIVisibleFields();
            ModelChangedHandler(true);
            MonoUtilities.RefreshPartContextWindow(part);
        }

        private void SetUIVisibleFields()
        {
            Fields[nameof(panelScale)].guiActiveEditor = !lengthWidth;
            Fields[nameof(panelLength)].guiActiveEditor = (lengthWidth && maxLength != minLength);
            Fields[nameof(panelWidth)].guiActiveEditor = (lengthWidth && maxLength != minLength);
            this.ROLupdateUIFloatEditControl(nameof(panelScale), 0.1f, 100f, largeStep, smallStep, slideStep);
            this.ROLupdateUIFloatEditControl(nameof(panelLength), minLength, maxLength, largeStep, smallStep, slideStep);
            this.ROLupdateUIFloatEditControl(nameof(panelWidth), minWidth, maxWidth, largeStep, smallStep, slideStep);
        }

        private void SetMaxTechLevel()
        {

            (Fields[nameof(TechLevel)].uiControlEditor as UI_FloatRange).maxValue = maxTechLevel;
            if (HighLogic.LoadedSceneIsEditor && TechLevel < 0)
            {
                TechLevel = maxTechLevel;
            }
            if (maxTechLevel == 0)
            {
                Fields[nameof(TechLevel)].guiActiveEditor = false;
                Fields[nameof(tlText)].guiActiveEditor = true;
            }
        }

        private void UpdateModulePositions()
        {
            if (lengthWidth)
                coreModule.setScaleForHeightAndDiameter(panelLength, panelWidth, true);
            else
                coreModule.SetScale(panelScale, panelScale);
            coreModule.SetPosition(coreModule.moduleHeight / 2);
            coreModule.UpdateModelScalesAndLayoutPositions(false);
        }

        private void UpdateAnimationAndTracking()
        {
            isTracking = stl.isTracking && trackingToggle && coreModule.definition.isTracking;
            trackingMode = TrackingMode.SUN;

            animationName = coreModule.definition.animationName;
            FindAnimations();

            // Allow StartFSM() to configure the animation state (time/speed/weight)
            // Change handler will need to re-call StartFSM() to properly reset a model.

            pivotName = coreModule.definition.pivotName;
            panelRotationTransform = part.FindModelTransform(pivotName);
            secondaryTransformName = raycastTransformName = coreModule.definition.secondaryTransformName;
            hasPivot = panelRotationTransform is Transform;
            originalRotation = currentRotation = panelRotationTransform?.localRotation ?? Quaternion.identity;
        }

        private void UpdateMassAndCost()
        {
            if (!lengthWidth)
            {
                area = coreModule.definition.panelArea * panelScale * panelScale;
            }
            else
            {
                float lengthScale = panelLength / coreModule.definition.panelLength;
                float widthScale = panelWidth / coreModule.definition.panelWidth;
                area = coreModule.definition.panelArea * lengthScale * widthScale;
            }

            mass = area * stl.kgPerM2;
            cost = area * stl.costPerM2;
            (float massMult, float costMult) = solarPanelType switch
            {
                "hinged" => (stl.massMultHinged, stl.costMultHinged),
                "folded" => (stl.massMultFolded, stl.costMultFolded),
                "tracking" => (stl.massMultTrack, stl.costMultTrack),
                _ => (1, 1)
            };
            mass = (mass * massMult) + addMass;
            cost = (cost * costMult) + addCost;
            mass = Math.Max(mass, 0.0001f);
            cost = Math.Max(cost, 0.1f);
        }

        private void UpdateAttachNodes(bool userInput)
        {
            coreModule.updateAttachNodeBody(coreNodeNames, userInput);

            if (part.srfAttachNode is AttachNode node
                && coreModule?.definition is ROLModelDefinition def
                && def.surfaceNode is AttachNodeBaseData surfNodeData)
            {
                float x = def.surfaceNode.position.x * (lengthWidth ? panelWidth / def.panelWidth : panelScale);
                float y = def.surfaceNode.position.y * (lengthWidth ? panelLength / def.panelLength : panelScale);
                float z = def.surfaceNode.position.z * (lengthWidth ? panelWidth / def.panelWidth : panelScale);
                Vector3 pos = new Vector3(x, y, z);
                ROLAttachNodeUtils.UpdateAttachNodePosition(part, node, pos, surfNodeData.orientation, userInput, node.size);
            }
        }

        /// <summary>
        /// Update the UI visibility for the currently available selections.<para/>
        /// Will hide/remove UI fields for slots with only a single option (models, textures, layouts).
        /// </summary>
        private void UpdateAvailableVariants() => coreModule.updateSelections();

        /// <summary>
        /// Calls the generic ROT procedural drag-cube updating routines.  Will update the drag cubes for whatever the current model state is.
        /// </summary>
        private void UpdateDragCubes() => ROLModInterop.OnPartGeometryUpdate(part, ShapeKey);

        private string ShapeKey => lengthWidth ? $"ROS|{currentVariant}|{currentCore}|{panelWidth}|{panelLength}|{trackingToggle}" : $"ROS|{currentVariant}|{currentCore}|{panelScale}|{trackingToggle}";

        #endregion Custom Update Methods

        #region Custom Methods

        public void RecalculateStats()
        {
            chargeRate = currentRate = area * stl.kwPerM2;
            var outResource = resHandler.outputResources.First(x => x.id == PartResourceLibrary.ElectricityHashcode);
            outResource.rate = chargeRate;

            ReloadTimeCurve();
        }

        public void ReloadTimeCurve()
        {
            timeEfficCurve = new FloatCurve();
            timeEfficCurve.ROLloadSingleLine(stl.key1);
            timeEfficCurve.ROLloadSingleLine(stl.key20);
            timeEfficCurve.ROLloadSingleLine(stl.key80);
            timeEfficCurve.ROLloadSingleLine(stl.key99);
        }

        private void FindAnimations()
        {
            anim = null;
            if (!string.IsNullOrEmpty(animationName))
            {
                Animation[] animations = part.transform.ROLFindRecursive("model").GetComponentsInChildren<Animation>();
                anim = animations.FirstOrDefault(x => x.GetClip(animationName) is AnimationClip);
                anim ??= animations.FirstOrDefault();
            }
            useAnimation = anim != null;
        }

        private static void CreateLineMaterial()
        {
            if (!lineMaterial)
            {
                // Unity's built-in shader for drawing simple colored things.
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                // Alpha blending: On.  Backface culling: Off.  Depth Writes: Off.
                lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                lineMaterial.SetInt("_ZWrite", 0);
            }
        }

        private void MakeRenderer(Transform parent, out GameObject go, out LineRenderer rend, string name = "ROSolarRenderer", Color color = default)
        {
            if (color == default) color = Color.red;
            go = new GameObject(name);
            go.transform.SetParent(parent);
            rend = go.AddComponent<LineRenderer>();
            rend.material = new Material(lineMaterial) { color = color };
            rend.startColor = rend.endColor = color;
            rend.startWidth = 0.05f;
            rend.endWidth = 0.01f;
            rend.enabled = false;
        }

        private void RemakeTrackingPointers()
        {
            if (trackingDrawer) Destroy(trackingDrawer);
            if (sunDirDrawer) Destroy(sunDirDrawer);
            if (panelOrientationDrawer) Destroy(panelOrientationDrawer);
            MakeRenderer(gameObject.transform, out trackingDrawer, out trackingRenderer, "ROSolarTrackingDir", Color.red);
            MakeRenderer(gameObject.transform, out sunDirDrawer, out sunDirRenderer, "ROSolarSunDir", Color.yellow);
            MakeRenderer(gameObject.transform, out panelOrientationDrawer, out panelRotRenderer, "ROSolarPanelDir", Color.green);
        }

        private void DrawRays()
        {
            // Of interest:  raycastTransform, secondaryTransform, panelRotationTransform, sunDir
            // secondaryTransform is for raycasts.
            // trackingDotTransform = useRaycastForTrackingDot ? secondaryTransform : panelRotationTransform;
            trackingRenderer.enabled = drawDebugRays;
            sunDirRenderer.enabled = drawDebugRays;
            panelRotRenderer.enabled = drawDebugRays;

            if (drawDebugRays)
            {
                Vector3 pivot = panelRotationTransform.position;
                sunDirRenderer.positionCount = 2;
                Vector3 sunDir = (trackingTransformLocal.position - pivot).normalized;
                sunDirRenderer.SetPosition(0, pivot);
                sunDirRenderer.SetPosition(1, pivot + sunDir);

                if (trackingDotTransform)
                {
                    trackingRenderer.positionCount = 2;
                    trackingRenderer.SetPosition(0, trackingDotTransform.position);
                    trackingRenderer.SetPosition(1, trackingDotTransform.position + trackingDotTransform.forward);
                }

                panelRotRenderer.positionCount = 2;
                panelRotRenderer.SetPosition(0, panelRotationTransform.position);
                panelRotRenderer.SetPosition(1, panelRotationTransform.position + panelRotationTransform.forward);
            }
        }

        #endregion Custom Methods
    }
}
