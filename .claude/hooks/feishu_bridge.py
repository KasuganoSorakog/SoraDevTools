"""
飞书双向通信桥接服务 - 本地 WebSocket 长连接模式

功能：
  1. 通过 lark-oapi SDK 建立 WebSocket 长连接接收飞书消息（无需公网）
  2. 用户在飞书回复机器人时，调用 claude --resume 继续工作
  3. 支持特殊指令：状态、停止、截图
  4. 将 Claude 执行状态回复给飞书用户

使用方式：
  pip install lark-oapi mss Pillow
  python feishu_bridge.py

飞书后台配置：
  1. 事件订阅 → 订阅方式选择「长连接（WebSocket）」
  2. 添加事件：im.message.receive_v1
  3. 权限：im:message:send_as_bot、im:message.p2p_msg:readonly、im:resource（上传图片）
"""

import io
import json
import logging
import os
import subprocess
import sys
import threading
import time
from pathlib import Path

import lark_oapi as lark
from lark_oapi.api.im.v1 import (
    CreateImageRequest,
    CreateImageRequestBody,
    CreateMessageRequest,
    CreateMessageRequestBody,
    P2ImMessageReceiveV1,
)

# ===== 配置 =====
APP_ID = "cli_a90314d54e38dbd9"
APP_SECRET = "HtzCSquRtWLzuM6Z8TQFOhfVr6YYIfXt"
HOOKS_DIR = Path(__file__).parent.resolve()
SESSION_FILE = HOOKS_DIR / "last_session.txt"
CWD_FILE = HOOKS_DIR / "last_cwd.txt"
# ================

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
)
logger = logging.getLogger("feishu_bridge")

# 当前正在执行的 Claude 进程
_active_process: subprocess.Popen | None = None
_process_lock = threading.Lock()

# 消息去重：按 chat_id + 指令文本 记录最近处理时间，冷却期内忽略重复
_last_command_time: dict[str, float] = {}  # key: "chat_id:command_text", value: timestamp
_dedup_lock = threading.Lock()
DEDUP_COOLDOWN = 30  # 同一聊天同一指令的冷却时间（秒）


def get_last_session() -> str | None:
    """读取最近一次 Claude Code 会话 ID。"""
    if SESSION_FILE.exists():
        text = SESSION_FILE.read_text(encoding="utf-8-sig").strip()
        return text if text else None
    return None


def get_last_cwd() -> str:
    """读取最近一次 Claude Code 工作目录。"""
    if CWD_FILE.exists():
        text = CWD_FILE.read_text(encoding="utf-8-sig").strip()
        if text:
            return text
    return str(HOOKS_DIR.parent.parent)  # 回退到项目根目录


def send_feishu_reply(client: lark.Client, chat_id: str, text: str):
    """向飞书聊天发送文本消息。"""
    body = CreateMessageRequestBody()
    body.msg_type = "text"
    body.receive_id = chat_id
    body.content = json.dumps({"text": text}, ensure_ascii=False)

    req = CreateMessageRequest.builder().receive_id_type("chat_id").request_body(body).build()
    resp = client.im.v1.message.create(req)
    if not resp.success():
        logger.error("Reply failed: code=%d, msg=%s", resp.code, resp.msg)


def capture_screenshot() -> bytes | None:
    """截取屏幕，返回 PNG 图片字节数据。"""
    try:
        import mss
        from PIL import Image

        with mss.mss() as sct:
            # 截取所有显示器的合并画面
            monitor = sct.monitors[0]  # 0 = 所有显示器合并
            screenshot = sct.grab(monitor)
            img = Image.frombytes("RGB", screenshot.size, screenshot.bgra, "raw", "BGRX")
            # 缩放大图以减小文件体积（飞书图片上传限制 10MB）
            max_width = 1920
            if img.width > max_width:
                ratio = max_width / img.width
                img = img.resize((max_width, int(img.height * ratio)), Image.LANCZOS)
            buf = io.BytesIO()
            img.save(buf, format="PNG", optimize=True)
            return buf.getvalue()
    except ImportError:
        logger.error("Screenshot requires 'mss' and 'Pillow': pip install mss Pillow")
        return None
    except Exception:
        logger.exception("Screenshot capture failed")
        return None


def upload_image_to_feishu(client: lark.Client, image_data: bytes) -> str | None:
    """上传图片到飞书，返回 image_key。"""
    image_file = io.BytesIO(image_data)
    body = CreateImageRequestBody.builder().image_type("message").image(image_file).build()
    req = CreateImageRequest.builder().request_body(body).build()
    resp = client.im.v1.image.create(req)
    if resp.success():
        return resp.data.image_key
    logger.error("Image upload failed: code=%d, msg=%s, log_id=%s", resp.code, resp.msg, resp.get_log_id())
    return None


def send_feishu_image(client: lark.Client, chat_id: str, image_key: str):
    """向飞书聊天发送图片消息。"""
    body = CreateMessageRequestBody()
    body.msg_type = "image"
    body.receive_id = chat_id
    body.content = json.dumps({"image_key": image_key})

    req = CreateMessageRequest.builder().receive_id_type("chat_id").request_body(body).build()
    resp = client.im.v1.message.create(req)
    if not resp.success():
        logger.error("Image reply failed: code=%d, msg=%s", resp.code, resp.msg)


def handle_screenshot_command(client: lark.Client, chat_id: str):
    """处理「截图」指令，截取屏幕并发送到飞书。"""
    send_feishu_reply(client, chat_id, "[飞书桥接服务] 正在截取屏幕...")
    image_data = capture_screenshot()
    if not image_data:
        send_feishu_reply(client, chat_id, "[飞书桥接服务] 截图失败，请确认已安装 mss 和 Pillow：pip install mss Pillow")
        return
    image_key = upload_image_to_feishu(client, image_data)
    if not image_key:
        send_feishu_reply(client, chat_id, "[飞书桥接服务] 图片上传飞书失败，请检查应用权限 im:resource。")
        return
    send_feishu_image(client, chat_id, image_key)
    logger.info("Screenshot sent to chat %s (image_key=%s)", chat_id, image_key)


def handle_status_command(client: lark.Client, chat_id: str):
    """处理「状态」指令。"""
    session_id = get_last_session()
    with _process_lock:
        running = _active_process is not None and _active_process.poll() is None

    lines = []
    lines.append("[飞书桥接服务] 当前状态")
    lines.append(f"  会话 ID: {session_id or '无'}")
    lines.append(f"  工作目录: {get_last_cwd()}")
    lines.append(f"  Claude 运行中: {'是' if running else '否'}")
    send_feishu_reply(client, chat_id, "\n".join(lines))


def handle_stop_command(client: lark.Client, chat_id: str):
    """处理「停止」指令，终止当前 Claude 进程。"""
    global _active_process
    with _process_lock:
        if _active_process is not None and _active_process.poll() is None:
            _active_process.terminate()
            _active_process = None
            send_feishu_reply(client, chat_id, "[飞书桥接服务] 已终止当前 Claude 执行。")
        else:
            send_feishu_reply(client, chat_id, "[飞书桥接服务] 当前没有正在执行的 Claude 任务。")


def invoke_claude(client: lark.Client, chat_id: str, user_message: str):
    """调用 claude --resume 继续工作，并将结果回复给飞书。"""
    global _active_process

    session_id = get_last_session()
    if not session_id:
        send_feishu_reply(client, chat_id, "[飞书桥接服务] 没有找到可恢复的会话 ID，请先在本地使用 Claude Code 完成一次任务。")
        return

    with _process_lock:
        if _active_process is not None and _active_process.poll() is None:
            send_feishu_reply(client, chat_id, "[飞书桥接服务] Claude 正在执行中，请等待完成或发送「停止」终止当前任务。")
            return

    cwd = get_last_cwd()
    cmd = ["claude", "--resume", session_id, "-p", user_message]
    logger.info("Invoking: %s (cwd=%s)", " ".join(cmd), cwd)
    send_feishu_reply(client, chat_id, f"[飞书桥接服务] 正在调用 Claude Code...\n指令: {user_message}")

    def _run():
        global _active_process
        try:
            with _process_lock:
                _active_process = subprocess.Popen(
                    cmd,
                    cwd=cwd,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    encoding="utf-8-sig",
                    errors="replace",
                    shell=True,
                )
            proc = _active_process
            output_lines = []
            for line in proc.stdout:
                output_lines.append(line.rstrip("\n"))
            proc.wait()

            output = "\n".join(output_lines)
            max_len = 3000
            if len(output) > max_len:
                output = output[:max_len] + "\n... (输出已截断)"

            status = "完成" if proc.returncode == 0 else f"异常退出 (code={proc.returncode})"
            reply = f"[Claude Code] 执行{status}\n\n{output}" if output.strip() else f"[Claude Code] 执行{status}（无输出）"
            send_feishu_reply(client, chat_id, reply)
        except Exception as e:
            logger.exception("Claude invocation failed")
            send_feishu_reply(client, chat_id, f"[飞书桥接服务] 调用 Claude 失败: {e}")
        finally:
            with _process_lock:
                _active_process = None

    threading.Thread(target=_run, daemon=True).start()


def extract_text_content(event_data: dict) -> str | None:
    """从事件数据中提取文本消息内容。"""
    try:
        message = event_data["event"]["message"]
        msg_type = message.get("message_type", "")
        if msg_type != "text":
            return None
        content = json.loads(message["content"])
        return content.get("text", "").strip()
    except (KeyError, json.JSONDecodeError):
        return None


def extract_chat_id(event_data: dict) -> str | None:
    """从事件数据中提取 chat_id。"""
    try:
        return event_data["event"]["message"]["chat_id"]
    except KeyError:
        return None



def make_event_handler(client: lark.Client):
    """创建消息事件处理函数。"""

    def handle_message(data: P2ImMessageReceiveV1):
        try:
            event_data = json.loads(lark.JSON.marshal(data))
        except Exception:
            logger.error("Failed to parse event data")
            return

        text = extract_text_content(event_data)
        chat_id = extract_chat_id(event_data)

        if not text or not chat_id:
            logger.info("Ignored non-text or unparseable message")
            return

        # 时间窗口去重：同一聊天的同一指令在冷却期内只处理一次
        dedup_key = f"{chat_id}:{text.strip()}"
        now = time.time()
        with _dedup_lock:
            last_time = _last_command_time.get(dedup_key, 0)
            if now - last_time < DEDUP_COOLDOWN:
                logger.info("Skipped (cooldown): %s (%.1fs since last)", text, now - last_time)
                return
            _last_command_time[dedup_key] = now

        logger.info("Received message: %s (chat=%s)", text, chat_id)

        # 所有指令处理放到后台线程，确保事件回调立即返回，避免 SDK 超时重试
        def _dispatch():
            cmd = text.strip()
            if cmd in ("状态", "status"):
                handle_status_command(client, chat_id)
            elif cmd in ("停止", "stop"):
                handle_stop_command(client, chat_id)
            elif cmd in ("截图", "screenshot"):
                handle_screenshot_command(client, chat_id)
            else:
                invoke_claude(client, chat_id, text)

        threading.Thread(target=_dispatch, daemon=True).start()

    return handle_message


def main():
    logger.info("Starting Feishu Bridge Service...")
    logger.info("Session file: %s", SESSION_FILE)
    logger.info("Working directory fallback: %s", get_last_cwd())

    # 创建 lark 客户端
    client = lark.Client.builder().app_id(APP_ID).app_secret(APP_SECRET).build()

    # 创建事件处理器
    handler = make_event_handler(client)

    # 注册事件并启动 WebSocket 长连接
    event_handler = (
        lark.EventDispatcherHandler.builder("", "")
        .register_p2_im_message_receive_v1(handler)
        .build()
    )

    ws_client = lark.ws.Client(
        APP_ID,
        APP_SECRET,
        event_handler=event_handler,
        log_level=lark.LogLevel.INFO,
    )

    logger.info("Connecting to Feishu WebSocket...")
    try:
        ws_client.start()
    except KeyboardInterrupt:
        logger.info("Shutting down...")


if __name__ == "__main__":
    main()
