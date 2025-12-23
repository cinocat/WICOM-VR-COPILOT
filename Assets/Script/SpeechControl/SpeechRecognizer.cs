using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SpeechRecognizerPlugin;

public class SpeechRecognizer : MonoBehaviour, ISpeechRecognizerPlugin
{
    [SerializeField] private Button startListeningBtn = null;
    [SerializeField] private Button stopListeningBtn = null;
    [SerializeField] private Toggle continuousListeningTgle = null;
    [SerializeField] private TMP_Dropdown languageDropdown = null;

    // Không dùng nữa (đã ép cố định 1 kết quả để tăng tốc)
    [SerializeField] private TMP_InputField maxResultsInputField = null;

    [SerializeField] private TextMeshProUGUI resultsTxt = null;
    [SerializeField] private TextMeshProUGUI errorsTxt = null;

    // Send to ChatManager
    [SerializeField] private ChatManager chatManager;

    private SpeechRecognizerPlugin plugin = null;
    private string lastRecognizedText = "";

    // Background/active mode control
    private bool isActiveMode = false;
    private float timeoutSeconds = 2f; // Cho UI quay về trạng thái nền nhanh hơn
    private Coroutine timeoutCoroutine = null;

    private void Start()
    {
        plugin = SpeechRecognizerPlugin.GetPlatformPluginVersion(this.gameObject.name);

        if (startListeningBtn != null) startListeningBtn.onClick.AddListener(StartListening);
        if (stopListeningBtn != null) stopListeningBtn.onClick.AddListener(StopListening);
        if (continuousListeningTgle != null) continuousListeningTgle.onValueChanged.AddListener(SetContinuousListening);
        if (languageDropdown != null) languageDropdown.onValueChanged.AddListener(SetLanguage);

        // Ép luôn continuous listening để sẵn sàng nhận
        //plugin.SetContinuousListening(true);

        // Ép chỉ 1 kết quả tốt nhất để giảm độ trễ/chi phí xử lý
        plugin.SetMaxResultsForNextRecognition(1);

        // Bắt đầu nghe ngay
        //plugin.StartListening();

        // Nếu vẫn muốn hiển thị UI maxResults, cập nhật về "1"
        if (maxResultsInputField != null)
        {
            maxResultsInputField.text = "1";
            maxResultsInputField.interactable = false; // khóa chỉnh sửa để đảm bảo luôn nhanh nhất
        }
    }

    private void StartListening()
    {
        plugin.StartListening();
    }

    private void StopListening()
    {
        plugin.StopListening();
    }

    private void SetContinuousListening(bool isContinuous)
    {
        plugin.SetContinuousListening(isContinuous);
    }

    private void SetLanguage(int dropdownValue)
    {
        if (languageDropdown == null) return;

        string newLanguage = languageDropdown.options[dropdownValue].text;
        plugin.SetLanguageForNextRecognition(newLanguage);

        // Đảm bảo vẫn 1 kết quả sau khi đổi ngôn ngữ
        plugin.SetMaxResultsForNextRecognition(1);
    }

    // Không dùng giá trị nhập nữa, luôn ép về 1 để nhanh nhất
    private void SetMaxResults(string _)
    {
        plugin.SetMaxResultsForNextRecognition(1);
        if (maxResultsInputField != null)
        {
            maxResultsInputField.text = "1";
        }
    }

    public void OnResult(string recognizedResult)
    {
        // recognizedResult có dạng "best~alt2~alt3...", chỉ lấy best để tăng tốc
        if (string.IsNullOrEmpty(recognizedResult))
            return;

        // Cắt nhanh trước dấu '~'
        int tildeIndex = recognizedResult.IndexOf('~');
        string best = tildeIndex >= 0 ? recognizedResult.Substring(0, tildeIndex) : recognizedResult;
        best = best.Trim();

        if (string.IsNullOrEmpty(best))
            return;

        isActiveMode = true;

        // Cập nhật UI tối thiểu
        if (resultsTxt != null)
            resultsTxt.text = best;

        lastRecognizedText = best;
        Debug.Log("<b>RECOGNIZED: </b>" + lastRecognizedText);

        // Gửi sang ChatManager ngay khi có best result
        if (chatManager != null)
        {
            OnVoiceRecognized(lastRecognizedText);
            chatManager.SendVoiceCommand(lastRecognizedText);
        }

        // Reset timer trả UI về nền
        if (timeoutCoroutine != null)
            StopCoroutine(timeoutCoroutine);
        timeoutCoroutine = StartCoroutine(TimeoutToBackground());
    }

    public void OnVoiceRecognized(string text)
    {
        // Xử lý text nhận được từ SpeechRecognizer
        if (chatManager != null)
        {
            chatManager.AddMessageToChat(text, true);
        }
        // Có thể thực hiện mapping, gửi lên AI, hoặc điều khiển drone...
    }

    private IEnumerator TimeoutToBackground()
    {
        yield return new WaitForSeconds(timeoutSeconds);
        isActiveMode = false;
        if (resultsTxt != null)
            resultsTxt.text = "Background listening...";
    }

    public void OnError(string recognizedError)
    {
        ERROR error = (ERROR)int.Parse(recognizedError);
        switch (error)
        {
            case ERROR.UNKNOWN:
                Debug.Log("<b>ERROR: </b> Unknown");
                if (errorsTxt != null) errorsTxt.text += "Unknown";
                break;
            case ERROR.INVALID_LANGUAGE_FORMAT:
                Debug.Log("<b>ERROR: </b> Language format is not valid");
                if (errorsTxt != null) errorsTxt.text += "Language format is not valid";
                break;
            default:
                break;
        }
    }
}