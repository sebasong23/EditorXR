#if UNITY_EDITORVR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.EditorVR;
using UnityEngine.Experimental.EditorVR.Actions;
using UnityEngine.Experimental.EditorVR.Core;
using UnityEngine.Experimental.EditorVR.Menus;
using UnityEngine.Experimental.EditorVR.Modules;
using UnityEngine.Experimental.EditorVR.Tools;
using UnityEngine.Experimental.EditorVR.Utilities;
using UnityEngine.InputNew;

namespace UnityEditor.Experimental.EditorVR
{
	partial class EditorVR
	{
		const byte kMinStencilRef = 2;

		readonly HashSet<object> m_ConnectedInterfaces = new HashSet<object>();

		byte stencilRef
		{
			get { return m_StencilRef; }
			set
			{
				m_StencilRef = (byte)Mathf.Clamp(value, kMinStencilRef, byte.MaxValue);

				// Wrap
				if (m_StencilRef == byte.MaxValue)
					m_StencilRef = kMinStencilRef;
			}
		}
		byte m_StencilRef = kMinStencilRef;

		void ConnectInterfaces(object obj, InputDevice device)
		{
			Transform rayOrigin = null;
			var deviceData = m_DeviceData.FirstOrDefault(dd => dd.inputDevice == device);
			if (deviceData != null)
				rayOrigin = deviceData.rayOrigin;
			
			ConnectInterfaces(obj, rayOrigin);
		}

		void ConnectInterfaces(object obj, Transform rayOrigin = null)
		{
			if (!m_ConnectedInterfaces.Add(obj))
				return;

			var connectInterfaces = obj as IConnectInterfaces;
			if (connectInterfaces != null)
				connectInterfaces.connectInterfaces = ConnectInterfaces;

			if (rayOrigin)
			{
				var ray = obj as IUsesRayOrigin;
				if (ray != null)
					ray.rayOrigin = rayOrigin;

				var usesProxy = obj as IUsesProxyType;
				if (usesProxy != null)
				{
					var deviceData = m_DeviceData.FirstOrDefault(dd => dd.rayOrigin == rayOrigin);
					if (deviceData != null)
						usesProxy.proxyType = deviceData.proxy.GetType();
				}

				var menuOrigins = obj as IUsesMenuOrigins;
				if (menuOrigins != null)
				{
					Transform mainMenuOrigin;
					var proxy = GetProxyForRayOrigin(rayOrigin);
					if (proxy != null && proxy.menuOrigins.TryGetValue(rayOrigin, out mainMenuOrigin))
					{
						menuOrigins.menuOrigin = mainMenuOrigin;
						Transform alternateMenuOrigin;
						if (proxy.alternateMenuOrigins.TryGetValue(rayOrigin, out alternateMenuOrigin))
							menuOrigins.alternateMenuOrigin = alternateMenuOrigin;
					}
				}
			}

			// Specific proxy ray setting
			var customRay = obj as ICustomRay;
			if (customRay != null)
			{
				customRay.showDefaultRay = ShowRay;
				customRay.hideDefaultRay = HideRay;
			}

			var lockableRay = obj as IUsesRayLocking;
			if (lockableRay != null)
			{
				lockableRay.lockRay = LockRay;
				lockableRay.unlockRay = UnlockRay;
			}

			var locomotion = obj as ILocomotor;
			if (locomotion != null)
				locomotion.viewerPivot = VRView.viewerPivot;

			var instantiateUI = obj as IInstantiateUI;
			if (instantiateUI != null)
				instantiateUI.instantiateUI = InstantiateUI;

			var createWorkspace = obj as ICreateWorkspace;
			if (createWorkspace != null)
				createWorkspace.createWorkspace = CreateWorkspace;

			var instantiateMenuUI = obj as IInstantiateMenuUI;
			if (instantiateMenuUI != null)
				instantiateMenuUI.instantiateMenuUI = InstantiateMenuUI;

			var raycaster = obj as IUsesRaycastResults;
			if (raycaster != null)
				raycaster.getFirstGameObject = GetFirstGameObject;

			var highlight = obj as ISetHighlight;
			if (highlight != null)
				highlight.setHighlight = m_HighlightModule.SetHighlight;

			var placeObjects = obj as IPlaceObject;
			if (placeObjects != null)
				placeObjects.placeObject = PlaceObject;

			var locking = obj as IUsesGameObjectLocking;
			if (locking != null)
			{
				locking.setLocked = m_LockModule.SetLocked;
				locking.isLocked = m_LockModule.IsLocked;
			}

			var positionPreview = obj as IGetPreviewOrigin;
			if (positionPreview != null)
				positionPreview.getPreviewOriginForRayOrigin = GetPreviewOriginForRayOrigin;

			var selectionChanged = obj as ISelectionChanged;
			if (selectionChanged != null)
				m_SelectionChanged += selectionChanged.OnSelectionChanged;

			var toolActions = obj as IActions;
			if (toolActions != null)
			{
				var actions = toolActions.actions;
				foreach (var action in actions)
				{
					var actionMenuData = new ActionMenuData()
					{
						name = action.GetType().Name,
						sectionName = ActionMenuItemAttribute.kDefaultActionSectionName,
						priority = int.MaxValue,
						action = action,
					};
					m_ActionsModule.menuActions.Add(actionMenuData);
				}
				UpdateAlternateMenuActions();
			}

			var directSelection = obj as IUsesDirectSelection;
			if (directSelection != null)
				directSelection.getDirectSelection = m_DirectSelection.GetDirectSelection;

			var grabObjects = obj as IGrabObjects;
			if (grabObjects != null)
			{
				grabObjects.canGrabObject = m_DirectSelection.CanGrabObject;
				grabObjects.objectGrabbed += m_DirectSelection.OnObjectGrabbed;
				grabObjects.objectsDropped += m_DirectSelection.OnObjectsDropped;
			}

			var spatialHash = obj as IUsesSpatialHash;
			if (spatialHash != null)
			{
				spatialHash.addToSpatialHash = m_SpatialHashModule.AddObject;
				spatialHash.removeFromSpatialHash = m_SpatialHashModule.RemoveObject;
			}

			var deleteSceneObjects = obj as IDeleteSceneObject;
			if (deleteSceneObjects != null)
				deleteSceneObjects.deleteSceneObject = DeleteSceneObject;

			var usesViewerBody = obj as IUsesViewerBody;
			if (usesViewerBody != null)
				usesViewerBody.isOverShoulder = IsOverShoulder;

			var mainMenu = obj as IMainMenu;
			if (mainMenu != null)
			{
				mainMenu.menuTools = m_MainMenuTools;
				mainMenu.menuWorkspaces = m_AllWorkspaceTypes.ToList();
				mainMenu.isToolActive = IsToolActive;
			}

			var alternateMenu = obj as IAlternateMenu;
			if (alternateMenu != null)
				alternateMenu.menuActions = m_ActionsModule.menuActions;

			var usesProjectFolderData = obj as IUsesProjectFolderData;
			if (usesProjectFolderData != null)
				m_ProjectFolderModule.AddConsumer(usesProjectFolderData);

			var usesHierarchyData = obj as IUsesHierarchyData;
			if (usesHierarchyData != null)
				m_HierarchyModule.AddConsumer(usesHierarchyData);

			var filterUI = obj as IFilterUI;
			if (filterUI != null)
				m_ProjectFolderModule.AddConsumer(filterUI);

			// Tracked Object action maps shouldn't block each other so we share an instance
			var trackedObjectMap = obj as ITrackedObjectActionMap;
			if (trackedObjectMap != null)
				trackedObjectMap.trackedObjectInput = m_DeviceInputModule.trackedObjectInput;

			var selectTool = obj as ISelectTool;
			if (selectTool != null)
				selectTool.selectTool = SelectTool;

			var usesViewerPivot = obj as IUsesViewerPivot;
			if (usesViewerPivot != null)
				usesViewerPivot.viewerPivot = U.Camera.GetViewerPivot();

			var usesStencilRef = obj as IUsesStencilRef;
			if (usesStencilRef != null)
			{
				byte? stencilRef = null;

				var mb = obj as MonoBehaviour;
				if (mb)
				{
					var parent = mb.transform.parent;
					if (parent)
					{
						// For workspaces and tools, it's likely that the stencil ref should be shared internally
						var parentStencilRef = parent.GetComponentInParent<IUsesStencilRef>();
						if (parentStencilRef != null)
							stencilRef = parentStencilRef.stencilRef;
					}
				}

				usesStencilRef.stencilRef = stencilRef ?? RequestStencilRef();
			}

			var selectObject = obj as ISelectObject;
			if (selectObject != null)
			{
				selectObject.getSelectionCandidate = m_SelectionModule.GetSelectionCandidate;
				selectObject.selectObject = m_SelectionModule.SelectObject;
			}

			var manipulatorVisiblity = obj as IManipulatorVisibility;
			if (manipulatorVisiblity != null)
				m_ManipulatorVisibilities.Add(manipulatorVisiblity);

			var setManipulatorsVisible = obj as ISetManipulatorsVisible;
			if (setManipulatorsVisible != null)
				setManipulatorsVisible.setManipulatorsVisible = SetManipulatorsVisible;

			var requestStencilRef = obj as IRequestStencilRef;
			if (requestStencilRef != null)
				requestStencilRef.requestStencilRef = RequestStencilRef;

			// Internal interfaces
			var forEachRayOrigin = obj as IForEachRayOrigin;
			if (forEachRayOrigin != null && IsSameAssembly<IForEachRayOrigin>(obj))
				forEachRayOrigin.forEachRayOrigin = ForEachRayOrigin;
		}

		static bool IsSameAssembly<T>(object obj)
		{
			// Until we move EditorVR into it's own assembly, this is a way to enforce 'internal' on interfaces
			var objType = obj.GetType();
			return objType.Assembly == typeof(T).Assembly;
		}

		void DisconnectInterfaces(object obj)
		{
			m_ConnectedInterfaces.Remove(obj);

			var selectionChanged = obj as ISelectionChanged;
			if (selectionChanged != null)
				m_SelectionChanged -= selectionChanged.OnSelectionChanged;

			var toolActions = obj as IActions;
			if (toolActions != null)
			{
				m_ActionsModule.RemoveActions(toolActions.actions);
				UpdateAlternateMenuActions();
			}

			var grabObjects = obj as IGrabObjects;
			if (grabObjects != null)
			{
				grabObjects.objectGrabbed -= m_DirectSelection.OnObjectGrabbed;
				grabObjects.objectsDropped -= m_DirectSelection.OnObjectsDropped;
			}

			var usesProjectFolderData = obj as IUsesProjectFolderData;
			if (usesProjectFolderData != null)
				m_ProjectFolderModule.RemoveConsumer(usesProjectFolderData);

			var usesHierarchy = obj as IUsesHierarchyData;
			if (usesHierarchy != null)
				m_HierarchyModule.RemoveConsumer(usesHierarchy);

			var filterUI = obj as IFilterUI;
			if (filterUI != null)
				m_ProjectFolderModule.RemoveConsumer(filterUI);

			var manipulatorVisiblity = obj as IManipulatorVisibility;
			if (manipulatorVisiblity != null)
				m_ManipulatorVisibilities.Remove(manipulatorVisiblity);
		}

		void PlaceObject(Transform obj, Vector3 targetScale)
		{
			foreach (var miniWorld in m_MiniWorlds)
			{
				if (!miniWorld.Contains(obj.position))
					continue;

				var referenceTransform = miniWorld.referenceTransform;
				obj.transform.parent = null;
				obj.position = referenceTransform.position + Vector3.Scale(miniWorld.miniWorldTransform.InverseTransformPoint(obj.position), miniWorld.referenceTransform.localScale);
				obj.rotation = referenceTransform.rotation * Quaternion.Inverse(miniWorld.miniWorldTransform.rotation) * obj.rotation;
				obj.localScale = Vector3.Scale(Vector3.Scale(obj.localScale, referenceTransform.localScale), miniWorld.miniWorldTransform.lossyScale);
				return;
			}

			m_ObjectPlacementModule.PlaceObject(obj, targetScale);
		}

		void DeleteSceneObject(GameObject sceneObject)
		{
			var renderers = sceneObject.GetComponentsInChildren<Renderer>(true);
			foreach (var renderer in renderers)
			{
				m_SpatialHashModule.spatialHash.RemoveObject(renderer);
			}

			U.Object.Destroy(sceneObject);
		}

		byte RequestStencilRef()
		{
			return stencilRef++;
		}
	}
}
#endif
