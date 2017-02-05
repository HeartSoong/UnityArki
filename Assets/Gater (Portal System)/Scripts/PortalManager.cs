using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
#if UNITY_EDITOR
	using UnityEditorInternal;
	using UnityEditor;
#endif

[DisallowMultipleComponent]
[ExecuteInEditMode]

[RequireComponent (typeof(MeshFilter))]
[RequireComponent (typeof(MeshRenderer))]
[RequireComponent (typeof(Rigidbody))]

public class PortalManager : MonoBehaviour {
	//General vars
	public GameObject SecondGate;
	public Material SecondGateCustomSkybox;
	public bool EnablePortalTrigger = true;
	public bool EnableMeshClipPlane = true;
	public Vector2 ProjectionResolution = new Vector2(1280, 1024);
	public enum ProjectionDepthQualityEnum {Min, Max};
	public ProjectionDepthQualityEnum ProjectionDepthQuality = ProjectionDepthQualityEnum.Max;
	private ProjectionDepthQualityEnum[] CurrentProjectionDepthQuality = new ProjectionDepthQualityEnum[0];
	[Range(1, 20)] public int RecursionSteps = 1;
	public Material CustomFinalRecursion;
	public GameObject[] ExcludedObjsFromTrigger = new GameObject[0];
	[Range(2, 31)] public int ExcludedObjsLayer = 2;
	public bool NextSceneAsyncLoad;
	public int NextSceneIndex = 0;
	//----------

	private Material[] GateMaterial;
	private RenderTexture[] RenTex;
	private Material ClipPlaneMaterial;
	private Material CloneClipPlaneMaterial;
	[HideInInspector] public GameObject ClipPlanePosObj;
	private Vector2[] CurrentProjectionResolution;
	[HideInInspector] public GameObject[] GateCamObjs;
	private int[] InitGateCamObjsCullingMask;
	private GameObject SceneviewRender;

	void OnEnable () {
		#if UNITY_EDITOR
			RenTex = new RenderTexture[2];
		#else
			RenTex = new RenderTexture[1];
		#endif
		GateMaterial = new Material [RenTex.Length];
		CurrentProjectionResolution = new Vector2[RenTex.Length];
		CurrentProjectionDepthQuality = new ProjectionDepthQualityEnum[RenTex.Length];

		GateCamObjs = new GameObject[20];
		InitGateCamObjsCullingMask = new int[GateCamObjs.Length];

		for (int i = 0; i < GateMaterial.Length; i++) //Generate "Portal" and "Clipping plane" materials
			if (!GateMaterial [i])
				GateMaterial [i] = new Material (Shader.Find ("Gater/UV Remap"));

		Shader ClipPlaneShader = Shader.Find ("Custom/StandardClippable");

		if (!ClipPlaneMaterial) {
			ClipPlaneMaterial = new Material (Shader.Find ("Standard"));
			ClipPlaneMaterial.shader = ClipPlaneShader;
		}
		if (!CloneClipPlaneMaterial) {
			CloneClipPlaneMaterial = new Material (Shader.Find ("Standard"));
			ClipPlaneMaterial.shader = ClipPlaneShader;
		}

		for (int j = 0; j < CurrentProjectionResolution.Length; j++)
			CurrentProjectionResolution [j] = new Vector2 (0, 0);

		//Apply custom settings to the portal components
		GetComponent<MeshRenderer> ().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		GetComponent<MeshRenderer> ().receiveShadows = false;
		GetComponent<MeshRenderer> ().sharedMaterial = GateMaterial [0];
		GetComponent<Rigidbody> ().mass = 1;
		GetComponent<Rigidbody> ().drag = 0;
		GetComponent<Rigidbody> ().angularDrag = 0;
		GetComponent<Rigidbody> ().useGravity = false;
		GetComponent<Rigidbody> ().isKinematic = true;
		GetComponent<Rigidbody> ().interpolation = RigidbodyInterpolation.None;
		GetComponent<Rigidbody> ().collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
		GetComponent<Rigidbody> ().constraints = RigidbodyConstraints.None;
		if (GetComponent<MeshCollider> ()) {
			GetComponent<MeshCollider> ().convex = true;
			GetComponent<MeshCollider> ().sharedMaterial = null;
		}

		//Disable collision of walls behind portals, and check if the excluded objects from trigger have a collider component
		if (ExcludedObjsFromTrigger.Length > 0)
			for (int k = 0; k < ExcludedObjsFromTrigger.Length; k++)
				if (ExcludedObjsFromTrigger [k]) {
					Physics.IgnoreCollision (transform.GetComponent<Collider> (), ExcludedObjsFromTrigger [k].GetComponent<Collider> (), true);

					if (!ExcludedObjsFromTrigger [k].GetComponent<Collider> ())
						Debug.LogError ("One excluded wall doesn't have a collider component");
				}

		#if UNITY_EDITOR
			EditorApplication.update = Update;

			//Search already existing required objects for teleport, and fill the relative variables with
			int GateCamObjsSteps = 0;
			
			for (int l = 0; l < transform.GetComponentsInChildren<Transform> ().Length; l++) {
				if (transform.GetComponentsInChildren<Transform> () [l].name == this.gameObject.name + " Camera " + GateCamObjsSteps) {
					GateCamObjs [GateCamObjsSteps] = transform.GetComponentsInChildren<Transform> () [l].gameObject;
						
					GateCamObjsSteps += 1;
				}
				
				if (transform.GetComponentsInChildren<Transform> () [l].name == transform.name + " SceneviewRender")
					SceneviewRender = transform.GetComponentsInChildren<Transform> () [l].gameObject;
				
				if (transform.GetComponentsInChildren<Transform> () [l].name == transform.name + " ClipPlanePosObj")
					ClipPlanePosObj = transform.GetComponentsInChildren<Transform> () [l].gameObject;
			}
		#endif
	}

	void Update () {
		#if UNITY_EDITOR
				SetGate ();

				GateCamRepos ();
		#endif
	}
	void FixedUpdate () {
		SetGate ();
	}
	void LateUpdate () {
		GateCamRepos ();
	}

	private Camera InGameCamera;
	private RenderTexture TempRenTex;
	private Mesh GateMesh;

	void SetGate () {
		if (SecondGate) {
			if (!InGameCamera) {
				ResetVars (false); //Reset arrays elements of the required objects for teleport, if game camera variable is null and any object is still colliding with the portal

				InGameCamera = Camera.main; //Fill empty "InGameCamera" variable with main camera
			} else {
				if (InGameCamera.nearClipPlane > .01f)
					Debug.LogError ("The nearClipPlane of 'Main Camera' is not equal to 0.01");

				GateMesh = GetComponent<MeshFilter> ().sharedMesh; //Acquire current portal mesh

				//Generate render texture for the portal camera
				for (int i = 0; i < RenTex.Length; i++) {
					if (CurrentProjectionResolution [i].x != ProjectionResolution.x || CurrentProjectionResolution [i].y != ProjectionResolution.y || CurrentProjectionDepthQuality [i] != ProjectionDepthQuality) {
						if (RenTex [i]) {
							#if UNITY_EDITOR
								if (!EditorApplication.isPlaying) {
									DestroyImmediate (RenTex [i], false);

									if (i == 0)
										DestroyImmediate (TempRenTex, false);
								}
								if (EditorApplication.isPlaying) {
									Destroy (RenTex [i]);
									
									if (i == 0)
										Destroy (TempRenTex);
								}
							#else
								Destroy (RenTex [i]);
								
								if (i == 0)
									Destroy (TempRenTex);
							#endif
						}
						if (!RenTex [i]) {
							RenTex [i] = new RenderTexture (Convert.ToInt32 (ProjectionResolution.x), Convert.ToInt32 (ProjectionResolution.y), ProjectionDepthQuality == ProjectionDepthQualityEnum.Min ? 16 : 24);
							RenTex [i].name = this.gameObject.name + " RenderTexture " + i;
							if (i == 0)
								TempRenTex = RenderTexture.GetTemporary (Convert.ToInt32 (ProjectionResolution.x), Convert.ToInt32 (ProjectionResolution.y), ProjectionDepthQuality == ProjectionDepthQualityEnum.Min ? 16 : 24);

							CurrentProjectionResolution [i] = new Vector2 (ProjectionResolution.x, ProjectionResolution.y);
							CurrentProjectionDepthQuality [i] = ProjectionDepthQuality;
						}
					}
				}

				#if UNITY_EDITOR
					LayerMask SceneTabLayerMask = Tools.visibleLayers;

					SceneTabLayerMask &= ~(1 << 1); //Disable SceneviewRender layer on Sceneview

					Tools.visibleLayers = SceneTabLayerMask;

					//Generate projection plane for Sceneview
					if (!SceneviewRender) {
						SceneviewRender = new GameObject (transform.name + " SceneviewRender");

						SceneviewRender.AddComponent<MeshFilter> ();
						SceneviewRender.AddComponent<MeshRenderer> ();

						SceneviewRender.transform.position = transform.position;
						SceneviewRender.transform.rotation = transform.rotation;
						SceneviewRender.transform.localScale = transform.localScale;
						SceneviewRender.transform.parent = transform;
					} else {
						if (SceneviewRender.name != transform.name + " SceneviewRender")
							SceneviewRender.name = transform.name + " SceneviewRender";

						SceneviewRender.layer = 4;

						SceneviewRender.transform.localPosition = new Vector3 (0, 0, .0001f);

						SceneviewRender.GetComponent<MeshFilter> ().sharedMesh = GateMesh;
						SceneviewRender.GetComponent<MeshRenderer> ().sharedMaterial = GateMaterial [1];

						//Apply render texture to the scene portal material
						if (GateMaterial.Length > 1)
							SceneviewRender.GetComponent<MeshRenderer> ().sharedMaterial.SetTexture ("_MainTex", InGameCamera && SecondGate.GetComponent<PortalManager> ().RenTex [1] ? SecondGate.GetComponent<PortalManager> ().RenTex [1] : null);
					}
				#endif

				//Apply render texture to the game portal material
				if (GateMaterial.Length > 0)
					GetComponent<MeshRenderer> ().sharedMaterial.SetTexture ("_MainTex", InGameCamera && SecondGate.GetComponent<PortalManager> ().RenTex [0] ? SecondGate.GetComponent<PortalManager> ().RenTex [0] : null);
				
				//Generate camera for the portal rendering
				for (int j = 0; j < GateCamObjs.Length; j++) {
					if (j < RecursionSteps) {
						if (!GateCamObjs [j]) {
							GateCamObjs [j] = new GameObject (transform.name + " Camera " + j);

							GateCamObjs [j].tag = "Untagged";

							GateCamObjs [j].transform.parent = transform;
							GateCamObjs [j].AddComponent<Camera> ();
							GateCamObjs [j].GetComponent<Camera> ().enabled = false;
							InitGateCamObjsCullingMask [j] = GateCamObjs [j].GetComponent<Camera> ().cullingMask;
							GateCamObjs [j].GetComponent<Camera> ().nearClipPlane = .01f;

							GateCamObjs [j].AddComponent<Skybox> ();
						} else {
							if (GateCamObjs [j].name != transform.name + " Camera " + j)
								GateCamObjs [j].name = transform.name + " Camera " + j;

							if (GateCamObjs [j].GetComponent<Camera> ().depth != InGameCamera.depth - 1)
								GateCamObjs [j].GetComponent<Camera> ().depth = InGameCamera.depth - 1;

							//Acquire settings from Scene/Game camera, to apply on Portal camera
							if (InGameCamera) {
								GateCamObjs [j].GetComponent<Camera> ().renderingPath = InGameCamera.renderingPath;
								GateCamObjs [j].GetComponent<Camera> ().useOcclusionCulling = InGameCamera.useOcclusionCulling;
								GateCamObjs [j].GetComponent<Camera> ().hdr = InGameCamera.hdr;
							}
						}

						if (SecondGate.GetComponent<PortalManager> ().GateCamObjs [j])
							SecondGate.GetComponent<PortalManager> ().GateCamObjs [j].GetComponent<Skybox> ().material = CustomFinalRecursion && (j > 0 && j == RecursionSteps - 1) ? CustomFinalRecursion : SecondGateCustomSkybox;
					} else {
						#if UNITY_EDITOR
							if (!EditorApplication.isPlaying)
								DestroyImmediate (GateCamObjs [j], false);
							if (EditorApplication.isPlaying)
								Destroy (GateCamObjs [j]);
						#else
							Destroy (GateCamObjs [j]);
						#endif
					}
				}

				//Generate mesh clip plane modificator object
				if (!ClipPlanePosObj) {
					ClipPlanePosObj = new GameObject (transform.name + " ClipPlanePosObj");

					ClipPlanePosObj.transform.position = transform.position;
					ClipPlanePosObj.transform.rotation = transform.rotation;
					ClipPlanePosObj.transform.parent = transform;
				} else {
					ClipPlanePosObj.transform.localPosition = new Vector3 (0, 0, .005f);

					if (ClipPlanePosObj.name != transform.name + " ClipPlanePosObj")
						ClipPlanePosObj.name = transform.name + " ClipPlanePosObj";
				}

				gameObject.layer = 1;

				//Apply current portal mesh to the mesh collider if exist
				if (GetComponent<MeshCollider> () && GetComponent<MeshCollider> ().sharedMesh != GateMesh)
					GetComponent<MeshCollider> ().sharedMesh = GateMesh;
				//Disable trigger of portal collider
				if (GetComponent<Collider> ()) {
					if (GetComponent<Collider> ().isTrigger != (InGameCamera ? true : false))
						GetComponent<Collider> ().isTrigger = InGameCamera ? (EnablePortalTrigger ? true : false) : false;
				} else
					Debug.LogError ("No collider component found");
			}
		}
	}

	void GateCamRepos () {
		if (InGameCamera && SecondGate) {
			Vector3[] GateCamPos = new Vector3[GateCamObjs.Length];
			Quaternion[] GateCamRot = new Quaternion[GateCamObjs.Length];

			for (int i = 0; i < RenTex.Length; i++) {
				if (RenTex [i]) {
					for (int j = RecursionSteps - 1; j >= 0; j--) {
						//Move portal camera to position/rotation of Scene/Game camera
						Camera SceneCamera = null;

						#if UNITY_EDITOR
							SceneCamera = SceneView.GetAllSceneCameras ().Length > 0 ? SceneView.GetAllSceneCameras () [0] : null;
						#endif

						GateCamObjs [j].GetComponent<Camera> ().aspect = (i == 1 && SceneCamera ? SceneCamera.aspect : InGameCamera.aspect);
						GateCamObjs [j].GetComponent<Camera> ().fieldOfView = (i == 1 && SceneCamera ? SceneCamera.fieldOfView : InGameCamera.fieldOfView);
						GateCamObjs [j].GetComponent<Camera> ().farClipPlane = (i == 1 && SceneCamera ? SceneCamera.farClipPlane : InGameCamera.farClipPlane);

						GateCamPos [j] = SecondGate.transform.InverseTransformPoint (i == 1 && SceneCamera ? SceneCamera.transform.position : InGameCamera.transform.position);

						GateCamPos [j].x = -GateCamPos [j].x;
						GateCamPos [j].z = -GateCamPos [j].z + j * (Vector3.Distance (transform.position, SecondGate.transform.position) / 5);

						GateCamRot [j] = Quaternion.Inverse (SecondGate.transform.rotation) * (i == 1 && SceneCamera ? SceneCamera.transform.rotation : InGameCamera.transform.rotation);

						GateCamRot [j] = Quaternion.AngleAxis (180.0f, new Vector3 (0, 1, 0)) * GateCamRot [j];

						GateCamObjs [j].transform.localPosition = GateCamPos [j];
						GateCamObjs [j].transform.localRotation = GateCamRot [j];

						//Render portal camera and recursion to render texture
						if (CustomFinalRecursion && (j > 0 && j == RecursionSteps - 1))
							GateCamObjs [j].GetComponent<Camera> ().cullingMask = 0;
						else {
							GateCamObjs [j].GetComponent<Camera> ().cullingMask = InGameCamera.cullingMask;
							GateCamObjs [j].GetComponent<Camera> ().cullingMask &= ~(1 << ExcludedObjsLayer);
							if (i == 0)
								GateCamObjs [j].GetComponent<Camera> ().cullingMask &= ~(1 << 4);
							else
								GateCamObjs [j].GetComponent<Camera> ().cullingMask &= ~(1 << 1);
						}

						GateCamObjs [j].GetComponent<Camera> ().targetTexture = TempRenTex;

						RenderTexture.active = GateCamObjs [j].GetComponent<Camera> ().targetTexture;

						GateCamObjs [j].GetComponent<Camera> ().Render ();

						Graphics.Blit (TempRenTex, RenTex [i]);

						RenderTexture.active = null;

						GateCamObjs [j].GetComponent<Camera> ().targetTexture = null;
					}
				}
			}
		}
	}

	class InitMaterialsList { public Material[] Materials; }
	private GameObject[] CollidedObjs = new GameObject[0];
	private string[] CollidedObjsInitName = new string[0];
	[HideInInspector] public Vector3 CollidedObjsParentPreviousFirstPos;
	[HideInInspector] public Vector3 CollidedObjsParentPreviousSecondPos;
	public bool AcquireNextPos;
	private InitMaterialsList[] CollidedObjsInitMaterials = new InitMaterialsList[0];
	private bool[] StandardObjShader = new bool[0];
	private bool[] CollidedObjsAlwaysTeleport = new bool[0];
	private bool[] CollidedObjsFirstTrig = new bool[0];
	private float[] CollidedObjsFirstTrigDist = new float[0];
	private GameObject[] ProxDetCollidedObjs = new GameObject[0];
	private GameObject[] CloneCollidedObjs = new GameObject[0];
	private Vector3[] CollidedObjVelocity = new Vector3[0];
	private bool[] ContinueTriggerEvents = new bool[0];
	[HideInInspector] public bool CollidedObjsExternalParent;
	[HideInInspector] public int EnterTriggerTimes;

	void OnTriggerEnter (Collider collision) {
		//Disable collision with objects excluded from trigger during teleport
		if (ExcludedObjsFromTrigger.Length > 0)
			for (int i = 0; i < ExcludedObjsFromTrigger.Length; i++)
				if (collision.GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ())
					Physics.IgnoreCollision (collision, ExcludedObjsFromTrigger [i].GetComponent<Collider> (), true);

		//Increment and partially fill the arrays elements of required object for teleport
		if (collision.gameObject != this.gameObject && !collision.GetComponent<PortalManager> () && !collision.name.Contains (collision.gameObject.GetHashCode ().ToString ()) && !collision.name.Contains ("Clone")) {
			Array.Resize (ref CollidedObjs, CollidedObjs.Length + 1);
			Array.Resize (ref CollidedObjsInitName, CollidedObjsInitName.Length + 1);
			Array.Resize (ref CollidedObjsInitMaterials, CollidedObjsInitMaterials.Length + 1);
			Array.Resize (ref StandardObjShader, StandardObjShader.Length + 1);
			Array.Resize (ref CollidedObjsAlwaysTeleport, CollidedObjsAlwaysTeleport.Length + 1);
			Array.Resize (ref CollidedObjsFirstTrig, CollidedObjsFirstTrig.Length + 1);
			Array.Resize (ref CollidedObjsFirstTrigDist, CollidedObjsFirstTrigDist.Length + 1);
			Array.Resize (ref ProxDetCollidedObjs, ProxDetCollidedObjs.Length + 1);
			Array.Resize (ref CloneCollidedObjs, CloneCollidedObjs.Length + 1);
			Array.Resize (ref CollidedObjVelocity, CollidedObjVelocity.Length + 1);
			Array.Resize (ref ContinueTriggerEvents, ContinueTriggerEvents.Length + 1);

			CollidedObjs [CollidedObjs.Length - 1] = collision.gameObject;
			CollidedObjsInitName [CollidedObjsInitName.Length - 1] = collision.gameObject.name;
			if (!collision.GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ()) {
				if (collision.transform.childCount > 0) {
					EnterTriggerTimes = 0;
					SecondGate.GetComponent<PortalManager> ().EnterTriggerTimes = 0;

					for (int j = 0; j < collision.GetComponentsInChildren<Transform> ().Length; j++)
						if (collision.GetComponentsInChildren<Transform> () [j] != collision.gameObject && collision.GetComponentsInChildren<Transform> () [j].GetComponent<Camera> ()) {
							CollidedObjsParentPreviousFirstPos = collision.GetComponentsInChildren<Transform> () [j].transform.localPosition;
							SecondGate.GetComponent<PortalManager> ().CollidedObjsParentPreviousFirstPos = collision.GetComponentsInChildren<Transform> () [j].transform.localPosition;

							AcquireNextPos = true;
							SecondGate.GetComponent<PortalManager> ().AcquireNextPos = true;
						}
				}
				if (!AcquireNextPos && collision.GetComponent<Camera> ()) {
					EnterTriggerTimes += 1;
					SecondGate.GetComponent<PortalManager> ().EnterTriggerTimes += 1;

					if (EnterTriggerTimes == 2)
						CollidedObjsExternalParent = false;
				}
				if (AcquireNextPos) {
					if (collision.transform.childCount == 0 && collision.gameObject && collision.GetComponent<Camera> ()) {
						CollidedObjsParentPreviousSecondPos = collision.transform.localPosition;
						SecondGate.GetComponent<PortalManager> ().CollidedObjsParentPreviousSecondPos = collision.transform.localPosition;

						AcquireNextPos = false;
						SecondGate.GetComponent<PortalManager> ().AcquireNextPos = false;
					}
				}
			}
			
			if (CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ()) {
				if (!StandardObjShader[CollidedObjs.Length - 1]) {
					if (CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterial.shader.name != "Standard")
						Debug.LogError ("The shader of object material is not 'Standard', mesh clippping will not be possible");
					if (CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterial.shader.name == "Standard")
						StandardObjShader[CollidedObjs.Length - 1] = true;
				}
				if (StandardObjShader[CollidedObjs.Length - 1]) {
					if (CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> () && EnableMeshClipPlane) {
						CollidedObjsInitMaterials [CollidedObjsInitMaterials.Length - 1] = new InitMaterialsList ();

						CollidedObjsInitMaterials [CollidedObjsInitMaterials.Length - 1].Materials = CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterials;

						CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterial = ClipPlaneMaterial;
						CollidedObjs [CollidedObjs.Length - 1].GetComponent<MeshRenderer> ().sharedMaterial.CopyPropertiesFromMaterial (CollidedObjsInitMaterials [CollidedObjs.Length - 1].Materials [0]);
					}
				}
			}

			ContinueTriggerEvents [ContinueTriggerEvents.Length - 1] = true;
		}
	}

	private GameObject[] ObjCollidedCamObj = new GameObject[2];
	private GameObject[] ObjCloneCollidedCamObj = new GameObject[2];

	void OnTriggerStay (Collider collision) {
		//Change position/rotation of required objects for teleport, and complete the fill of remaining arrays elements
		for (int i = 0; i < CollidedObjs.Length; i++) {
			if (ContinueTriggerEvents [i] && CollidedObjs [i]) {
				if (!ProxDetCollidedObjs [i]) {
					ProxDetCollidedObjs [i] = new GameObject (CollidedObjs [i].name + " Proximity Detector");

					ProxDetCollidedObjs [i].transform.position = transform.position;
					ProxDetCollidedObjs [i].transform.rotation = transform.rotation;
					ProxDetCollidedObjs [i].transform.parent = transform;
				}
				if (ProxDetCollidedObjs [i]) {
					if (GateMesh && ProxDetCollidedObjs [i]) {
						if (CollidedObjs [i].transform.childCount > 0) {
							for (int j = 0; j < CollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; j++)
								if (CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> () && CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].GetComponent<Camera> ())
									ObjCollidedCamObj [0] = CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].gameObject;
						}
						if (CollidedObjs [i].transform.childCount == 0)
							if (CollidedObjs [i].GetComponent<Camera> ())
								ObjCollidedCamObj [1] = CollidedObjs [i];
						
						Vector3 ProxDetCollidedObjPos = transform.InverseTransformPoint ((ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0] ? ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].transform.position : CollidedObjs [i].transform.position));
						Vector3 ProxDetCollLimit = new Vector3 (GateMesh.bounds.size.x / 2, GateMesh.bounds.size.y / 2, GateMesh.bounds.size.z / 2);

						ProxDetCollidedObjs [i].transform.localPosition = new Vector3 (ProxDetCollidedObjPos.x > -ProxDetCollLimit.x && ProxDetCollidedObjPos.x < ProxDetCollLimit.x ? ProxDetCollidedObjPos.x : ProxDetCollidedObjs [i].transform.localPosition.x, ProxDetCollidedObjPos.y > -ProxDetCollLimit.y && ProxDetCollidedObjPos.y < ProxDetCollLimit.y ? ProxDetCollidedObjPos.y : ProxDetCollidedObjs [i].transform.localPosition.y, ProxDetCollidedObjs [i].transform.localPosition.z);

						if (!CollidedObjsAlwaysTeleport [i]) {
							if (!CollidedObjsFirstTrig [i]) {
								CollidedObjsFirstTrigDist [i] = Vector3.Dot (CollidedObjs [i].transform.position - ProxDetCollidedObjs [i].transform.position, ProxDetCollidedObjs [i].transform.forward);

								CollidedObjsFirstTrig [i] = true;
							}
							if (CollidedObjsFirstTrig [i] && CollidedObjsFirstTrigDist [i] < 0) {
								CollidedObjsAlwaysTeleport [i] = true;
								
								CollidedObjs [i].name = CollidedObjs [i].name + " " + CollidedObjs [i].GetHashCode ().ToString ();
							}
						}
						if (CollidedObjsAlwaysTeleport [i]) {
							if (!CloneCollidedObjs [i]) {
								CloneCollidedObjs [i] = (GameObject)Instantiate (CollidedObjs [i], SecondGate.transform.position, SecondGate.transform.rotation);

								if (CloneCollidedObjs [i].GetComponent<AudioListener> ())
									Destroy (CloneCollidedObjs [i].GetComponent<AudioListener> ());
								
								if (CloneCollidedObjs [i].transform.childCount > 0) {
									for (int k = 0; k < CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; k++) {
										if (CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k] != CloneCollidedObjs [i].transform && CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<MeshRenderer> ())
											CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<MeshRenderer> ().enabled = false;

										if (CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<AudioListener> ())
											Destroy (CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<AudioListener> ());
									
										if (CloneCollidedObjs [i].transform.GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> () && CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<Camera> ())
											ObjCloneCollidedCamObj [0] = CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].gameObject;
										if (!CloneCollidedObjs [i].transform.GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> () && CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<Camera> ())
											CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].gameObject.GetComponent<Camera> ().enabled = false;
									}
								}
								if (CloneCollidedObjs [i].transform.childCount == 0)
									if (CloneCollidedObjs [i].GetComponent<Camera> ())
										ObjCloneCollidedCamObj [1] = CloneCollidedObjs [i];
								
								if (CloneCollidedObjs [i].GetComponent<MeshRenderer> () && StandardObjShader [i]) {
									if (EnableMeshClipPlane) {
										CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial = CloneClipPlaneMaterial;
										CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.CopyPropertiesFromMaterial (CollidedObjsInitMaterials [i].Materials [0]);
									}
									if (!EnableMeshClipPlane) {
										CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterials = CollidedObjsInitMaterials [i].Materials;
										CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterials = CollidedObjsInitMaterials [i].Materials;
									}
								}

								if (ObjCloneCollidedCamObj [0]) {
									CloneCollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().enabled = false;

									CloneCollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().m_OriginalCameraPosition = CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().m_OriginalCameraPosition;
								}

								CloneCollidedObjs [i].name = CollidedObjsInitName [i] + " Clone";

								CloneCollidedObjs [i].transform.position = SecondGate.transform.position;
								CloneCollidedObjs [i].transform.parent = SecondGate.transform;
							}
							if (CloneCollidedObjs [i]) {
								float DistAmount = .015f;

								float CollidedObjProxDetDistStay = Vector3.Dot ((ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0] ? ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].transform.position : CollidedObjs [i].transform.position) - ProxDetCollidedObjs [i].transform.position, ProxDetCollidedObjs [i].transform.forward);
								Vector3 CloneCollidedObjLocalPos = transform.InverseTransformPoint (CollidedObjs [i].transform.position);

								CloneCollidedObjLocalPos.x = -CloneCollidedObjLocalPos.x;
								CloneCollidedObjLocalPos.z = -CloneCollidedObjLocalPos.z - DistAmount;

								CloneCollidedObjs [i].transform.localPosition = CloneCollidedObjLocalPos;

								Quaternion CloneCollidedObjLocalRot = Quaternion.Inverse (transform.rotation) * (CollidedObjs [i].transform.rotation);

								CloneCollidedObjLocalRot = Quaternion.AngleAxis (180.0f, new Vector3 (0, -1, 0)) * CloneCollidedObjLocalRot;

								CloneCollidedObjs [i].transform.localRotation = CloneCollidedObjLocalRot;

								if (ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0] && ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0]) {
									if (!ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> ())
										ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].AddComponent<Skybox> ();
									if (ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> ()) {
										ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> ().material = SecondGateCustomSkybox;

										if (!SecondGateCustomSkybox) {
											#if UNITY_EDITOR
												if (!EditorApplication.isPlaying)
													DestroyImmediate (ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> (), false);
												if (EditorApplication.isPlaying)
													Destroy (ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> ());
											#else
												Destroy (ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> ());
											#endif
										}
									}

									ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].GetComponent<Camera> ().enabled = CollidedObjProxDetDistStay < -DistAmount ? true : false;
									ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].GetComponent<Camera> ().enabled = CollidedObjProxDetDistStay >= -DistAmount ? true : false;

									InGameCamera = ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].GetComponent<Camera> ().enabled ? ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].GetComponent<Camera> () : ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].GetComponent<Camera> ();

									if (ObjCloneCollidedCamObj [0]) {
										ObjCloneCollidedCamObj [0].transform.localPosition = ObjCollidedCamObj [0].transform.localPosition;
										ObjCloneCollidedCamObj [0].transform.localRotation = ObjCollidedCamObj [0].transform.localRotation;
									}
								}

								if (CollidedObjs [i].GetComponent<MeshRenderer> () && CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial == ClipPlaneMaterial) {
									Vector3 DirectionVector = Vector3.forward;

									CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.SetVector ("_planePos", ClipPlanePosObj.transform.position);
									CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.SetVector ("_planeNorm", Quaternion.Euler (transform.eulerAngles) * -DirectionVector);

									CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.SetVector ("_planePos", SecondGate.GetComponent<PortalManager> ().ClipPlanePosObj.transform.position);
									CloneCollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterial.SetVector ("_planeNorm", Quaternion.Euler (SecondGate.transform.eulerAngles) * -DirectionVector);
								}
							}
						}
					}
				}
			}
		}
	}

	private Vector3 PreviousCollidedObjsInternalParentPos;

	void OnTriggerExit (Collider collision) {
		//Destroy required objects for teleport, reset relative arrays, and move original collided object to the its final position/rotation
		for (int i = 0; i < CloneCollidedObjs.Length; i++) {
			if (ContinueTriggerEvents [i] && CollidedObjs [i] && CollidedObjs [i].GetHashCode ().ToString () == collision.gameObject.GetHashCode ().ToString () && CloneCollidedObjs [i]) {
				if (CollidedObjVelocity [i] == Vector3.zero)
					CollidedObjVelocity [i] = CollidedObjs [i].GetComponent<Rigidbody> () ? CollidedObjs [i].GetComponent<Rigidbody> ().velocity.magnitude * -SecondGate.transform.forward : new Vector3 (0, 0, 0);

				float CollObjProxDetDistExit = Vector3.Dot (CollidedObjs [i].transform.position - ProxDetCollidedObjs [i].transform.position, ProxDetCollidedObjs [i].transform.forward);

				GameObject[] CollidedObjsInternalParent = new GameObject[CollidedObjs [i].transform.childCount > 0 && !CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> () ? CollidedObjs [i].transform.childCount : 0];

				if (CollidedObjsInternalParent.Length > 0) {
					for (int j = 0; j < CollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; j++) {
						if (CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j] != CollidedObjs [i].transform && CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].GetComponent<Camera> ()) {
							CollidedObjsInternalParent [0] = CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].gameObject;
							PreviousCollidedObjsInternalParentPos = CollidedObjsInternalParent [0].transform.position;
						}
					}
				}

				if (CollidedObjs [i].transform.childCount > 0) {
					for (int k = 0; k < CollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; k++)
						if (CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> () && CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<Camera> ())
							ObjCollidedCamObj [0] = CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].gameObject;
				}
				if (CollidedObjs [i].transform.childCount == 0)
					if (CollidedObjs [i].GetComponent<Camera> ())
						ObjCollidedCamObj [1] = CollidedObjs [i];
				if (CloneCollidedObjs [i].transform.childCount > 0) {
					for (int k = 0; k < CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; k++)
						if (CloneCollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> () && CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].GetComponent<Camera> ())
							ObjCloneCollidedCamObj [0] = CloneCollidedObjs [i].transform.GetComponentsInChildren<Transform> () [k].gameObject;
				}
				if (CloneCollidedObjs [i].transform.childCount == 0)
					if (CloneCollidedObjs [i].GetComponent<Camera> ())
						ObjCloneCollidedCamObj [1] = CloneCollidedObjs [i];

				if (CollObjProxDetDistExit > 0) {
					if (!CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ()) {
						if (CollidedObjsInternalParent.Length == 0) {
							if (CollidedObjs [i].transform.parent != null) {
								if (!CollidedObjsExternalParent)
									CollidedObjs [i].transform.localPosition = CollidedObjsParentPreviousFirstPos;
								if (CollidedObjsExternalParent)
									CollidedObjs [i].transform.localPosition = CollidedObjsParentPreviousSecondPos;
							}
							if (CollidedObjs [i].transform.parent == null)
								CollidedObjs [i].transform.position = CloneCollidedObjs [i].transform.position;
						}
						if (CollidedObjsInternalParent.Length > 0) {
							CollidedObjs [i].transform.position = CloneCollidedObjs [i].transform.position;

							CollidedObjsInternalParent [0].transform.position = PreviousCollidedObjsInternalParentPos;
						}
					}
					if (CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ())
						CollidedObjs [i].transform.position = CloneCollidedObjs [i].transform.position;

					CollidedObjs [i].transform.rotation = CloneCollidedObjs [i].transform.rotation;

					if (ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0] && ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0]) {
						if (SecondGateCustomSkybox && ObjCloneCollidedCamObj [ObjCloneCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> () && !ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> ())
							ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].AddComponent<Skybox> ();
						if (ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> ())
							ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].GetComponent<Skybox> ().material = SecondGateCustomSkybox;
						
						ObjCollidedCamObj [ObjCollidedCamObj [1] ? 1 : 0].GetComponent<Camera> ().enabled = true;

						if (NextSceneAsyncLoad)
							LoadNextScene ();
					}
					if (CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ())
						CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().m_MouseLook.Init (CollidedObjs [i].transform, CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ().m_Camera.transform);

					if (CollidedObjVelocity [i] != new Vector3 (0, 0, 0))
						CollidedObjs [i].GetComponent<Rigidbody> ().velocity = CollidedObjVelocity [i];
				}

				CollidedObjs [i].name = CollidedObjsInitName [i];

				if (CollidedObjs [i].GetComponent<MeshRenderer> () && EnableMeshClipPlane && StandardObjShader [i]) {
					CollidedObjs [i].GetComponent<MeshRenderer> ().sharedMaterials = CollidedObjsInitMaterials [i].Materials;

					CollidedObjsInitMaterials [i].Materials = null;
				}

				CollidedObjs [i] = null;
				CollidedObjsInitName [i] = "";
				CollidedObjsAlwaysTeleport [i] = false;
				CollidedObjsFirstTrig [i] = false;
				CollidedObjsFirstTrigDist [i] = 0;
				Destroy (ProxDetCollidedObjs [i]);
				Destroy (CloneCollidedObjs [i]);
				CollidedObjVelocity [i] = new Vector3 (0, 0, 0);
				ContinueTriggerEvents [i] = false;
			}
		}

		if (collision.transform.childCount == 0 && collision.GetComponent<Camera> ()) {
			CollidedObjsExternalParent = true;
			SecondGate.GetComponent<PortalManager> ().CollidedObjsExternalParent = true;
		}
		if (collision.transform.childCount > 0 && !collision.GetComponent<Camera> ()) {
			CollidedObjsExternalParent = false;
			SecondGate.GetComponent<PortalManager> ().CollidedObjsExternalParent = false;
		}

		//Enable collision with objects excluded from trigger during teleport
		if (ExcludedObjsFromTrigger.Length > 0)
			for (int m = 0; m < ExcludedObjsFromTrigger.Length; m++)
				if (collision.GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ())
					Physics.IgnoreCollision (collision, ExcludedObjsFromTrigger [m].GetComponent<Collider> (), false);

		ResetVars (true);
	}

	void ResetVars (bool TriggerExit) {
		bool SetVars = false;

		if (CollidedObjs.Length > 0) {
			if (!TriggerExit) {
				for (int i = 0; i < CollidedObjs.Length; i++) {
					if (CloneCollidedObjs [i])
						Destroy (CloneCollidedObjs [i]);
					
					if (ProxDetCollidedObjs [i])
						Destroy (ProxDetCollidedObjs [i]);
					
					if (CollidedObjs [i] && CollidedObjs [i].transform.childCount > 0 && CollidedObjs [i].GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController> ()) {
						Camera CollObjCam = null;

						for (int j = 0; j < CollidedObjs [i].transform.GetComponentsInChildren<Transform> ().Length; j++)
							if (CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].GetComponent<Camera>())
								CollObjCam = CollidedObjs [i].transform.GetComponentsInChildren<Transform> () [j].GetComponent<Camera> ();

						if (CollObjCam && !CollObjCam.enabled)
							CollObjCam.enabled = true;
					}
				}

				SetVars = true;
			}
			if (TriggerExit) {
				int ElementsChecked = 0;

				for (int i = 0; i < CollidedObjs.Length; i++)
					if (!CollidedObjs [i])
						ElementsChecked += 1;

				if (ElementsChecked == CollidedObjs.Length)
					SetVars = true;

				ElementsChecked = 0;
			}
		}

		if (SetVars) {
			CollidedObjs = new GameObject[0];
			CollidedObjsInitName = new string[0];
			CollidedObjsInitMaterials = new InitMaterialsList[0];
			CollidedObjsAlwaysTeleport = new bool[0];
			CollidedObjsFirstTrig = new bool[0];
			CollidedObjsFirstTrigDist = new float[0];
			ProxDetCollidedObjs = new GameObject[0];
			CloneCollidedObjs = new GameObject[0];
			CollidedObjVelocity = new Vector3[0];
			ContinueTriggerEvents = new bool[0];
		}
	}

	IEnumerator LoadNextScene() {
		AsyncOperation AsyncLoad = SceneManager.LoadSceneAsync (NextSceneIndex);

		yield return AsyncLoad;
	}
}