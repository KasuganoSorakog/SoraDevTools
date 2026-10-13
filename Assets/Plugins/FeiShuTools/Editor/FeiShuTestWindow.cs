using UnityEditor;
using UnityEngine;

namespace SoraTools
{
    public class FeiShuTestWindow : EditorWindow
    {
        string _appId = "cli_a90314d54e38dbd9";
        string _appSecret = "HtzCSquRtWLzuM6Z8TQFOhfVr6YYIfXt";

        string[] _idTypes = { "open_id", "chat_id", "user_id", "email" };
        int _idTypeIndex = 1;
        string _receiveId = "";
        string _message = "这是一条来自 Unity 的测试消息";
        string _phoneNumber = "";

        FeiShuBot _bot;
        string _status = "";
        bool _statusIsSuccess;
        string _chatList = "";
        bool _showChatList;
        bool _showIdHelp;
        bool _showCredentials;
        int _sendMode; // 0=ID, 1=手机号

        Vector2 _scroll;

        // 缓存样式
        GUIStyle _headerStyle;
        GUIStyle _sectionTitleStyle;
        GUIStyle _statusSuccessStyle;
        GUIStyle _statusErrorStyle;
        GUIStyle _buttonStyle;
        GUIStyle _sendButtonStyle;
        GUIStyle _messageStyle;
        GUIStyle _chatListStyle;
        bool _stylesInited;

        [MenuItem("SoraTools/飞书机器人")]
        static void Open()
        {
            var win = GetWindow<FeiShuTestWindow>("飞书机器人");
            win.minSize = new Vector2(360, 400);
            win.Show();
        }

        void InitStyles()
        {
            if (_stylesInited) return;
            _stylesInited = true;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 36,
                normal = { textColor = new Color(0.42f, 0.68f, 1f) }
            };

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f) }
            };

            _statusSuccessStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 28,
                normal = { textColor = new Color(0.35f, 0.85f, 0.45f) }
            };

            _statusErrorStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 28,
                normal = { textColor = new Color(1f, 0.55f, 0.35f) }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fixedHeight = 26,
                fontStyle = FontStyle.Bold
            };

            _sendButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fixedHeight = 32,
                fontStyle = FontStyle.Bold
            };

            _messageStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 12
            };

            _chatListStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 10
            };
        }

        void EnsureBot()
        {
            if (_bot == null)
                _bot = new FeiShuBot(_appId, _appSecret);
        }

        void SetStatus(string msg, bool success)
        {
            _status = msg;
            _statusIsSuccess = success;
        }

        void OnGUI()
        {
            InitStyles();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // ---- 标题 ----
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("FeiShu Bot", _headerStyle);
            EditorGUILayout.Space(2);

            // ---- 状态栏 ----
            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.LabelField(_status, _statusIsSuccess ? _statusSuccessStyle : _statusErrorStyle);
                EditorGUILayout.Space(4);
            }

            // ---- 凭证（默认折叠） ----
            DrawSection("应用凭证", () =>
            {
                _showCredentials = EditorGUILayout.Foldout(_showCredentials, "编辑凭证", true);
                if (_showCredentials)
                {
                    _appId = EditorGUILayout.TextField("App ID", _appId);
                    _appSecret = EditorGUILayout.PasswordField("App Secret", _appSecret);
                }
            });

            // ---- 消息内容 ----
            DrawSection("消息内容", () =>
            {
                _message = EditorGUILayout.TextArea(_message, _messageStyle, GUILayout.Height(50));
            });

            // ---- 发送方式 ----
            DrawSection("发送消息", () =>
            {
                EditorGUILayout.Space(2);
                _sendMode = GUILayout.Toolbar(_sendMode, new[] { "  通过 ID 发送  ", "  通过手机号发送  " });
                EditorGUILayout.Space(6);

                if (_sendMode == 0)
                    DrawSendById();
                else
                    DrawSendByPhone();
            });

            // ---- 工具 ----
            DrawSection("工具", () =>
            {
                if (GUILayout.Button("查询机器人所在的群", _buttonStyle))
                {
                    EnsureBot();
                    var result = _bot.ListChats();
                    if (!string.IsNullOrEmpty(result))
                    {
                        _chatList = result;
                        _showChatList = true;
                        SetStatus("群列表已获取", true);
                    }
                    else
                    {
                        SetStatus("获取群列表失败，请查看 Console", false);
                    }
                }

                if (!string.IsNullOrEmpty(_chatList))
                {
                    EditorGUILayout.Space(2);
                    _showChatList = EditorGUILayout.Foldout(_showChatList, "查看群列表 JSON", true);
                    if (_showChatList)
                        EditorGUILayout.TextArea(_chatList, _chatListStyle, GUILayout.Height(80));
                }
            });

            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        void DrawSendById()
        {
            _idTypeIndex = EditorGUILayout.Popup("ID 类型", _idTypeIndex, _idTypes);
            _receiveId = EditorGUILayout.TextField("接收者 ID", _receiveId);

            EditorGUILayout.Space(2);
            _showIdHelp = EditorGUILayout.Foldout(_showIdHelp, "ID 格式说明", true);
            if (_showIdHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("chat_id", "oc_xxxxx  (群聊)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("open_id", "ou_xxxxx  (个人)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("email", "user@company.com", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(6);
            DrawColorButton("发送消息", new Color(0.35f, 0.65f, 1f), () =>
            {
                EnsureBot();
                if (string.IsNullOrEmpty(_receiveId))
                {
                    SetStatus("请填写接收者 ID", false);
                }
                else
                {
                    var ok = _bot.SendText(_idTypes[_idTypeIndex], _receiveId, _message);
                    SetStatus(ok ? "消息发送成功!" : "发送失败，请查看 Console", ok);
                }
            });
        }

        void DrawSendByPhone()
        {
            _phoneNumber = EditorGUILayout.TextField("手机号", _phoneNumber);
            EditorGUILayout.LabelField("  无需 +86 前缀，会自动补充", EditorStyles.miniLabel);

            EditorGUILayout.Space(6);
            DrawColorButton("发送消息", new Color(0.3f, 0.8f, 0.5f), () =>
            {
                EnsureBot();
                if (string.IsNullOrEmpty(_phoneNumber))
                {
                    SetStatus("请填写手机号", false);
                }
                else
                {
                    var ok = _bot.SendTextByPhone(_phoneNumber, _message);
                    SetStatus(ok ? "通过手机号发送成功!" : "发送失败，请查看 Console", ok);
                }
            });

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("  需要权限: contact:user.id:readonly", EditorStyles.miniLabel);
        }

        void DrawColorButton(string label, Color color, System.Action onClick)
        {
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            if (GUILayout.Button(label, _sendButtonStyle))
                onClick();
            GUI.backgroundColor = oldBg;
        }

        void DrawSection(string title, System.Action drawContent)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(title, _sectionTitleStyle);
            EditorGUILayout.Space(2);
            drawContent();
            EditorGUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }
    }
}
