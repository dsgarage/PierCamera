using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;

public class CameraFooterController : MonoBehaviour
{
    private Button shutterButton;
    private Button previewButton;
    private bool isRecording = false;
    private Coroutine recordCoroutine;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        shutterButton = root.Q<Button>("ShutterButton");
        previewButton = root.Q<Button>("PreviewButton");

        shutterButton.RegisterCallback<PointerDownEvent>(OnShutterDown);
        shutterButton.RegisterCallback<PointerUpEvent>(OnShutterUp);
        previewButton.clicked += OnPreviewClicked;
    }

    private void OnShutterDown(PointerDownEvent evt)
    {
        recordCoroutine = StartCoroutine(StartRecording());
    }

    private void OnShutterUp(PointerUpEvent evt)
    {
        if (isRecording)
        {
            StopRecording();
        }
        else
        {
            StopCoroutine(recordCoroutine);
            TakePhoto();
        }
    }

    private IEnumerator StartRecording()
    {
        yield return new WaitForSeconds(0.5f);
        isRecording = true;
        Debug.Log("🎬 録画開始");
        // ここで録画開始処理
    }

    private void StopRecording()
    {
        isRecording = false;
        Debug.Log("⏹ 録画停止");
        UpdatePreview("latest_video_thumbnail.png");
    }

    private void TakePhoto()
    {
        Debug.Log("📸 写真撮影");
        UpdatePreview("latest_photo_thumbnail.png");
    }

    private void UpdatePreview(string thumbnailPath)
    {
        var style = previewButton.style;
        style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>(thumbnailPath));
    }

    private void OnPreviewClicked()
    {
        Debug.Log("🖼 プレビュー表示");
        // 撮影済みメディアを開く処理
    }
}