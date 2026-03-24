using UnityEngine;
using TMPro;

/// <summary>
/// Reusable floating text component that billboards toward the camera.
/// Used by Worker, Warrior, and Enemy for state display.
/// </summary>
public class FloatingText : MonoBehaviour
{
    public float heightOffset = 2.5f;
    public float fontSize = 2f;
    public Color initialColor = Color.white;
    public string initialText = "";

    private GameObject textObject;
    private TextMeshPro tmp;
    private Camera cachedCamera;
    private string lastText;
    private Color lastColor;

    void Awake()
    {
        cachedCamera = Camera.main;

        textObject = new GameObject("FloatingText");
        textObject.transform.parent = transform;
        textObject.transform.localPosition = new Vector3(0, heightOffset, 0);

        tmp = textObject.AddComponent<TextMeshPro>();
        tmp.text = initialText;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = initialColor;
        tmp.enableAutoSizing = false;
        tmp.sortingOrder = 100;

        lastText = initialText;
        lastColor = initialColor;
    }

    void LateUpdate()
    {
        if (textObject == null) return;

        // Update position
        textObject.transform.position = transform.position + Vector3.up * heightOffset;

        // Billboard effect
        if (cachedCamera != null)
        {
            textObject.transform.LookAt(cachedCamera.transform);
            textObject.transform.Rotate(0, 180, 0);
        }
    }

    /// <summary>
    /// Update the displayed text and color. Skips TMP update if unchanged.
    /// </summary>
    public void SetText(string text, Color color)
    {
        if (tmp == null) return;

        if (text != lastText)
        {
            tmp.text = text;
            lastText = text;
        }

        if (color != lastColor)
        {
            tmp.color = color;
            lastColor = color;
        }
    }

    void OnDestroy()
    {
        if (textObject != null)
        {
            Destroy(textObject);
        }
    }
}
