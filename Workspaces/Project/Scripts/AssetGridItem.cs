﻿using System.Collections;
using System.IO;
using ListView;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VR.Handles;
using UnityEngine.VR.Utilities;
using Object = UnityEngine.Object;

public class AssetGridItem : ListViewItem<AssetData>
{
	private const float kMagnetizeDuration = 0.5f;
	private const float kPreviewDuration = 0.1f;
	//TODO: replace with a GrabOrigin transform once menu PR lands
	private readonly Vector3 kGrabPositionOffset = new Vector3(0f, 0.02f, 0.03f);
	private readonly Quaternion kGrabRotationOffset = Quaternion.AngleAxis(30f, Vector3.left);

	[SerializeField]
	private Text m_Text;

	[SerializeField]
	private BaseHandle m_Handle;

	[SerializeField]
	private Image m_TextPanel;

	[SerializeField]
	private Material m_NoClipCubeMaterial;

	[SerializeField]
	private Renderer m_Cube;

	private bool m_Setup;
	private Transform m_GrabbedObject;
	private Material m_GrabMaterial;
	private float m_GrabLerp;
	private float m_PreviewFade;
	private Transform m_PreviewObject;
	private float m_PreviewScale;

	private Coroutine m_TransitionCoroutine;

	public override void Setup(AssetData listData)
	{
		base.Setup(listData);
		// First time setup
		if (!m_Setup)
		{
			// Cube material might change, so we always instance it
			U.Material.GetMaterialClone(m_Cube);

			m_Handle.handleDragging += GrabBegin;
			m_Handle.handleDrag += GrabDrag;
			m_Handle.handleDragged += GrabEnd;

			m_Handle.hovering += OnBeginHover;
			m_Handle.hovered += OnEndHover;

			m_Setup = true;
		}

		InstantiatePreview();

		m_Text.text = Path.GetFileNameWithoutExtension(listData.path);

		var assetPath = AssetData.GetPathRelativeToAssets(data.path);
		var cachedIcon = AssetDatabase.GetCachedIcon(assetPath);
		if (cachedIcon)
		{
			cachedIcon.wrapMode = TextureWrapMode.Clamp;
			m_Cube.sharedMaterial.mainTexture = cachedIcon;
		}
	}

	public void SwapMaterials(Material textMaterial)
	{
		m_Text.material = textMaterial;
		m_TextPanel.material = textMaterial;
	}

	public void UpdateTransforms(float scale)
	{
		transform.localScale = Vector3.one * scale;

		var cameraTransform = U.Camera.GetMainCamera().transform;

		//Rotate text toward camera
		Vector3 eyeVector3 = Quaternion.Inverse(transform.parent.rotation) * cameraTransform.forward;
		eyeVector3.x = 0;
		if (Vector3.Dot(eyeVector3, Vector3.forward) > 0)
			m_TextPanel.transform.localRotation = Quaternion.LookRotation(eyeVector3, Vector3.up);
		else
			m_TextPanel.transform.localRotation = Quaternion.LookRotation(eyeVector3, Vector3.down);

		//Handle preview fade
		if (m_PreviewObject)
		{
			if (m_PreviewFade == 0)
			{
				m_PreviewObject.gameObject.SetActive(false);
				m_Cube.gameObject.SetActive(true);
				m_Cube.transform.localScale = Vector3.one;
			}
			else if (m_PreviewFade == 1)
			{
				m_PreviewObject.gameObject.SetActive(true);
				m_Cube.gameObject.SetActive(false);
				m_PreviewObject.transform.localScale = Vector3.one * m_PreviewScale;
			}
			else
			{
				m_Cube.gameObject.SetActive(true);
				m_PreviewObject.gameObject.SetActive(true);
				m_Cube.transform.localScale = Vector3.one * (1 - m_PreviewFade);
				m_PreviewObject.transform.localScale = Vector3.one * m_PreviewFade * m_PreviewScale;
			}
		}
	}

	private void InstantiatePreview()
	{
		if(m_PreviewObject)
			U.Object.Destroy(m_PreviewObject.gameObject);
		if (!data.preview)
			return;

		m_PreviewObject = Instantiate(data.preview).transform;
		m_PreviewObject.position = Vector3.zero;
		m_PreviewObject.rotation = Quaternion.identity;

		var totalBounds = new Bounds();
		var renderers = m_PreviewObject.GetComponentsInChildren<Renderer>(true);

		//Don't show a preview if there are no renderers
		if (renderers.Length == 0)
		{
			U.Object.Destroy(m_PreviewObject.gameObject);
			return;
		}
		//Normalize scale to 1
		foreach (var renderer in renderers)
			totalBounds.Encapsulate(renderer.bounds);

		m_PreviewObject.SetParent(transform, false);

		m_PreviewScale = 1 / Mathf.Max(totalBounds.size.x, totalBounds.size.y, totalBounds.size.z);
		m_PreviewObject.localPosition = Vector3.up * 0.5f;

		m_PreviewObject.gameObject.SetActive(false);
		m_PreviewObject.localScale = Vector3.zero;
	}

	public void GetMaterials(out Material textMaterial)
	{
		textMaterial = Object.Instantiate(m_Text.material);
	}

	public void Clip(Bounds bounds, Matrix4x4 parentMatrix)
	{
		m_Cube.sharedMaterial.SetMatrix("_ParentMatrix", parentMatrix);
		m_Cube.sharedMaterial.SetVector("_ClipExtents", bounds.extents * 5);
	}

	private void GrabBegin(BaseHandle baseHandle, HandleEventData eventData)
	{
		var clone = (GameObject) Instantiate(gameObject, transform.position, transform.rotation, transform.parent);
		var cloneItem = clone.GetComponent<AssetGridItem>();
		if(m_GrabMaterial)
			U.Object.Destroy(m_GrabMaterial);

		var cubeRenderer = cloneItem.m_Cube.GetComponent<Renderer>();
		cubeRenderer.sharedMaterial = m_NoClipCubeMaterial;
		m_GrabMaterial = U.Material.GetMaterialClone(cubeRenderer.GetComponent<Renderer>());
		m_GrabMaterial.mainTexture = m_Cube.sharedMaterial.mainTexture;
		m_GrabMaterial.color = Color.white;

		if (cloneItem.m_PreviewObject)
		{
			m_Cube.gameObject.SetActive(false);
			cloneItem.m_PreviewObject.gameObject.SetActive(true);
			cloneItem.m_PreviewObject.transform.localScale = Vector3.one;
		}

		m_GrabbedObject = clone.transform;
		m_GrabLerp = 0;
		StartCoroutine(Magnetize());
	}

	private IEnumerator Magnetize()
	{
		var startTime = Time.realtimeSinceStartup;
		var currTime = 0f;
		while (currTime < kMagnetizeDuration)
		{
			currTime = Time.realtimeSinceStartup - startTime;
			m_GrabLerp = currTime / kMagnetizeDuration;
			yield return null;
		}
		m_GrabLerp = 1;
	}

	private void GrabDrag(BaseHandle baseHandle, HandleEventData eventData)
	{
		var rayTransform = eventData.rayOrigin.transform;
		m_GrabbedObject.transform.position = Vector3.Lerp(m_GrabbedObject.transform.position, rayTransform.position + rayTransform.rotation * kGrabPositionOffset, m_GrabLerp);
		m_GrabbedObject.transform.rotation = Quaternion.Lerp(m_GrabbedObject.transform.rotation, rayTransform.rotation * kGrabRotationOffset, m_GrabLerp);
	}

	private void GrabEnd(BaseHandle baseHandle, HandleEventData eventData)
	{
		U.Object.Destroy(m_GrabbedObject.gameObject);
	}

	private void OnBeginHover(BaseHandle baseHandle, HandleEventData eventData)
	{
		if (gameObject.activeInHierarchy)
		{
			if(m_TransitionCoroutine != null)
				StopCoroutine(m_TransitionCoroutine);
			m_TransitionCoroutine = StartCoroutine(AnimatePreview(false));
		}
	}

	private void OnEndHover(BaseHandle baseHandle, HandleEventData eventData)
	{
		if (gameObject.activeInHierarchy)
		{
			if (m_TransitionCoroutine != null)
				StopCoroutine(m_TransitionCoroutine);
			m_TransitionCoroutine = StartCoroutine(AnimatePreview(true));
		}
	}

	private IEnumerator AnimatePreview(bool @out)
	{
		var startVal = 0;
		var endVal = 1;
		if (@out)
		{
			startVal = 1;
			endVal = 0;
		}
		var startTime = Time.realtimeSinceStartup;
		while (Time.realtimeSinceStartup - startTime < kPreviewDuration)
		{
			m_PreviewFade = Mathf.Lerp(startVal, endVal, (Time.realtimeSinceStartup - startTime) / kPreviewDuration);
			yield return null;
		}
		m_PreviewFade = endVal;
	}

	private void OnDestroy()
	{
		U.Object.Destroy(m_Cube.sharedMaterial);
		if(m_GrabMaterial)
			U.Object.Destroy(m_GrabMaterial);
	}
}