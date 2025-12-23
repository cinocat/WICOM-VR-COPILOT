using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System;

public class ChatManager : MonoBehaviour
{
    [SerializeField] private Communication communication;
    [SerializeField] private Telemetry telemetry;

    [Header("UI References")]
    public TMP_InputField inputField;
    public Button sendButton;
    public Button voiceButton;
    public TextMeshProUGUI statusText;
    public GameObject messagePrefab;
    public Transform messageContainer;
    public ScrollRect chatScrollRect;

    [Header("API Settings")]
    public string apiKey = "your_api_key_here";
    public string apiUrl = "https://api.deepseek.com/v1/chat/completions";

    private bool isWaitingForResponse = false;

    private string lastAIResult = "";

    void Start()
    {
        if (communication == null)
        {
            // Try to find Communication in parent
            communication = GetComponent<Communication>();
            if (communication == null)
            {
                Debug.LogError("Communication component not found in hierarchy.");
                enabled = false;
                return;
            }
        }

        var communicationObj = GameObject.FindGameObjectWithTag("Communication");
        if (communicationObj == null)
        {
            Debug.LogError("Communication GameObject not found. Please check the tag.");
            enabled = false;
            return;
        }

        // Find Telemetry as a child of Communication
        var telemetryTransform = communicationObj.transform.Find("Telemetry");
        if (telemetryTransform == null)
        {
            Debug.LogError("Telemetry child not found under Communication GameObject.");
            enabled = false;
            return;
        }

        telemetry = telemetryTransform.GetComponent<Telemetry>();
        if (telemetry == null)
        {
            Debug.LogError("Telemetry component not found on Telemetry GameObject.");
            enabled = false;
            return;
        }

        // Thiết lập button events
        sendButton.onClick.AddListener(OnSendButtonClick);
        voiceButton.onClick.AddListener(OnVoiceButtonClick);

        // Thiết lập input field
        inputField.onEndEdit.AddListener(OnInputEndEdit);

        statusText.text = "Sẵn sàng kết nối";
    }

    void Update()
    {
        // Enter để gửi tin nhắn
        if (Input.GetKeyDown(KeyCode.Return) && !isWaitingForResponse)
        {
            OnSendButtonClick();
        }
    }

    public void OnSendButtonClick()
    {
        if (string.IsNullOrEmpty(inputField.text) || isWaitingForResponse) return;

        //string mappingPrompt =
        //"Mapping text -> function:\n" +
        //"\"kết nối\" -> VCConnect\n" +
        //"\"chế độ offboard\" -> VCOffboardMode\n" +
        //"\"chế độ bay vòng tròn\" -> VCCircleStart\n" +
        //"\"dừng chế độ bay vòng tròn\" -> VCCircleStop\n\n" +
        //"text:";

        string fullmappingPrompt = "Khi nhận được văn bản mô tả, hãy trả về CHÍNH XÁC tên hàm tương ứng theo mapping dưới đây, " +
            "KHÔNG thêm bất kỳ giải thích, bình luận hay ký tự nào khác: " +
            "\r\n\r\n\"kết nối\" -> \"VCConnect\" " +
            "\r\n\r\n\"chế độ offboard\" -> \"VCOffboardMode\"  " +
            "\r\n\r\n\"chế độ bay vòng tròn\" -> \"VCCircleStart\"" +
            "\r\n\r\n \"dừng chế độ bay vòng tròn\" -> \"VCCircleStop\" " +
            "\r\n\r\nQUY TẮC: " +
            "\r\n\r\n1. Chỉ trả về tên hàm duy nhất, không thêm gì khác " +
            "\r\n2. Áp dụng cho cả các từ/cụm từ có nghĩa tương đương hoặc gần giống " +
            "\r\n3. Nếu không khớp với bất kỳ mô tả nào, trả về chuỗi rỗng \"\" " +
            "\r\n\r\nText:";

        string message = inputField.text;
        inputField.text = "";

        // Hiển thị tin nhắn người dùng
        AddMessageToChat(message, true);

        // Gửi prompt mapping + message lên AI
        string fullPrompt = fullmappingPrompt + " " + message;
        // Gửi đến DeepSeek
        //StartCoroutine(SendToDeepSeek(message));
        StartCoroutine(SendToDeepSeek(fullPrompt));
    }

    public void SendVoiceCommand(string recognizedText)
    {
        if (string.IsNullOrEmpty(recognizedText) || isWaitingForResponse) return;

        string fullmappingPrompt = 
            "Khi nhận được văn bản mô tả, hãy trả về CHÍNH XÁC tên hàm tương ứng theo mapping dưới đây, " +
            "KHÔNG thêm bất kỳ giải thích, bình luận hay ký tự nào khác: " +
            "\r\n\"kết nối\" -> \"VCConnect\" " +
            "\r\n\"chế độ khởi động\" -> \"VCArm\" " +
            "\r\n\"chế độ hạ cánh\" -> \"VCLand\" " +
            "\r\n\"chế độ bay trong nhà\" -> \"VCOffboardMode\"  " +
            "\r\n\"chế độ bay vòng tròn\" -> \"VCCircleStart\"" +
            "\r\n\"tạm dừng chế độ\" -> \"VCCircleStop\" " +
            "\r\nQUY TẮC: " +
            "\r\n1. Chỉ trả về tên hàm duy nhất, không thêm gì khác " +
            "\r\n2. Áp dụng cho cả các từ/cụm từ có nghĩa tương đương hoặc gần giống " +
            "\r\n3. Nếu không khớp với bất kỳ mô tả nào, trả về chuỗi rỗng \"\" " +
            "\r\nText:";

        // Hiển thị tin nhắn người dùng (giọng nói)
        AddMessageToChat(recognizedText, true);

        // Gửi prompt mapping + text nhận diện lên AI
        string fullPrompt = fullmappingPrompt + " " + recognizedText;
        StartCoroutine(SendToDeepSeek(fullPrompt));
    }

    public void OnVoiceButtonClick()
    {
        // Sẽ thêm nhận diện giọng nói sau
        AddMessageToChat("Tính năng giọng nói sẽ được thêm sau!", false);
    }

    void OnInputEndEdit(string text)
    {
        if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter))
        {
            OnSendButtonClick();
        }
    }

    public void AddMessageToChat(string message, bool isUser)
    {
        // Tạo tin nhắn mới từ prefab
        GameObject newMessage = Instantiate(messagePrefab, messageContainer);

        // Lấy component Text
        TextMeshProUGUI messageText = newMessage.GetComponentInChildren<TextMeshProUGUI>();
        if (messageText != null)
        {
            messageText.text = message;

            // Đổi màu theo người gửi
            if (isUser)
            {
                messageText.color = Color.blue;
                messageText.alignment = TextAlignmentOptions.Right;
            }
            else
            {
                messageText.color = Color.white;
                messageText.alignment = TextAlignmentOptions.Left;
            }
        }

        // Tự động cuộn xuống dưới
        StartCoroutine(ScrollToBottom());
    }

    IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }

    // Các class cho JSON

    IEnumerator SendToDeepSeek(string userMessage)
    {
        isWaitingForResponse = true;
        statusText.text = "Đang kết nối DeepSeek...";

        // Tạo danh sách tin nhắn, có thể thêm system message để thiết lập hành vi cho AI
        DeepSeekMessage[] messageList = new DeepSeekMessage[]
        {
        new DeepSeekMessage { role = "system", content = "Bạn là một trợ lý ảo hữu ích." }, // Tùy chọn
        new DeepSeekMessage { role = "user", content = userMessage }
        };

        // Chuẩn bị dữ liệu request
        DeepSeekRequest requestData = new DeepSeekRequest
        {
            model = "deepseek-chat",
            messages = messageList,
            temperature = 0.7,
            max_tokens = 2000 // DeepSeek hỗ trợ max token khá cao
        };

        string jsonData = JsonUtility.ToJson(requestData);
        byte[] rawData = System.Text.Encoding.UTF8.GetBytes(jsonData);

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(rawData);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            statusText.text = "Đang gửi yêu cầu...";
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text;
                //Debug.Log("JSON Response: " + jsonResponse); // Rất hữu ích để debug

                try
                {
                    // Sử dụng lớp Response đã điều chỉnh
                    DeepSeekResponse response = JsonUtility.FromJson<DeepSeekResponse>(jsonResponse);

                    if (response.choices != null && response.choices.Length > 0)
                    {
                        string aiResponse = response.choices[0].message.content;
                        lastAIResult = aiResponse; // Lưu kết quả trả ve

                        AddMessageToChat(aiResponse, false);
                        statusText.text = "Phản hồi nhận được";

                        // Thực thi function điều khiển dựa trên kết quả
                        ExecuteMappedFunction(aiResponse);
                    }
                    else
                    {
                        AddMessageToChat("Xin lỗi, tôi không có phản hồi cho yêu cầu này.", false);
                        statusText.text = "Lỗi: Phản hồi trống";
                    }
                }
                catch (Exception e)
                {
                    AddMessageToChat("Lỗi xử lý phản hồi từ AI.", false);
                    statusText.text = "Lỗi JSON: " + e.Message;
                    //Debug.LogError("JSON Parse Error: " + e.Message + "\nResponse: " + request.downloadHandler.text);
                }
            }
            else
            {
                AddMessageToChat("Lỗi kết nối: " + request.error, false);
                statusText.text = "HTTP Lỗi: " + request.responseCode;
                //Debug.LogError("API Request Failed: " + request.error + "\nResponse: " + request.downloadHandler.text);
            }
        }

        isWaitingForResponse = false;
        if (!isWaitingForResponse) // Chỉ hiện "Sẵn sàng" nếu không có request nào khác đang chờ
            statusText.text = "Sẵn sàng";
    }

    private void ExecuteMappedFunction(string functionName)
    {
        switch (functionName)
        {
            case "VCConnect":
                communication.StartUDP();
                break;
            case "VCArm":
                communication.SendCommandArmDisarm((byte)1); 
                break;
            case "VCLand":
                communication.SendCommandLand();
                break;
            case "VCOffboardMode":
                telemetry.SetOffboardMode();
                break;
            case "VCCircleStart":
                telemetry.VCCircleStart();
                break;
            case "VCCircleStop":
                telemetry.VCCircleStop();
                break;
            default:
                // Không khớp, không thực hiện gì
                break;
        }
    }

    [System.Serializable]
    public class DeepSeekRequest
    {
        public string model;
        public DeepSeekMessage[] messages;
        public double temperature;
        public int max_tokens;
    }

    [System.Serializable]
    public class DeepSeekMessage
    {
        public string role;
        public string content;
    }

    // LỚP CHO RESPONSE - SỬA LẠI CHO CHÍNH XÁC
    [System.Serializable]
    public class DeepSeekResponse
    {
        public string id;
        //public string object_name; // Lưu ý: 'object' là từ khóa trong C#, nên đổi tên
        [SerializeField] public string @object; // Sử dụng @ để escape keyword
        public int created;
        public Choice[] choices;
        public Usage usage;
    }

    [System.Serializable]
    public class Usage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    [System.Serializable]
    public class Choice
    {
        public int index;
        public Message message;
        public string finish_reason;
    }

    [System.Serializable]
    public class Message
    {
        public string role;
        public string content;
    }
}