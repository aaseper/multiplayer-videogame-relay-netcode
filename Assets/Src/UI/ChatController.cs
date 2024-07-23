using System;
using TMPro;
using UnityEngine;

[Serializable]
public class ChatController : MonoBehaviour
{
    [Header("Chat")] [SerializeField] public GameObject chatCanvas;
    [SerializeField] public TMP_Text chatBody;
    [SerializeField] public TMP_Text chatWriteMessage;
    [SerializeField] public GameObject chatBar;
    [SerializeField] public TMP_Text chatPlayerName;
    [SerializeField] public TMP_InputField chatMessage;

    [SerializeField] [HideInInspector] private string _username;

    private void Awake()
    {
        chatWriteMessage.enabled = !chatBar.activeSelf;
    }

    public void SubmitChatMessage(string username, string location, string message = null)
    {
        var msg = message ?? chatMessage.text;
        ReceiveChatMessage($"{username}:{location}$ {msg}");
        chatMessage.text = "";
    }

    public void ReceiveChatMessage(string message)
    {
        var msg = $"{message}\n{chatBody.text}";
        chatBody.text = msg;
    }

    public void WriteChatMessage()
    {
        chatBar.SetActive(!chatBar.activeSelf);
    }
    
    public void SetPlayerName(string username)
    {
        _username = username;
        chatPlayerName.text = _username;
    }
}