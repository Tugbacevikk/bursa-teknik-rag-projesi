(function() {
    // BTÜ Asistan Floating Chat Widget
    const WIDGET_HTML = `
        <div id="btu-widget-container" style="position: fixed; bottom: 20px; right: 20px; z-index: 999999; font-family: sans-serif;">
            <button id="btu-widget-toggle" style="background-color: #0d3b66; color: white; border: none; border-radius: 50%; width: 60px; height: 60px; font-size: 28px; cursor: pointer; box-shadow: 0 4px 12px rgba(0,0,0,0.3); display: flex; align-items: center; justify-content: center;">
                🎓
            </button>
            <div id="btu-chat-box" style="display: none; position: absolute; bottom: 75px; right: 0; width: 360px; height: 500px; background-color: white; border-radius: 12px; box-shadow: 0 5px 20px rgba(0,0,0,0.2); flex-direction: column; overflow: hidden; border: 1px solid #ddd;">
                <div style="background-color: #0d3b66; color: white; padding: 15px; font-weight: bold; display: flex; justify-content: space-between; align-items: center;">
                    <span>🎓 BTÜ Asistan Chatbot</span>
                    <button id="btu-chat-close" style="background: none; border: none; color: white; font-size: 18px; cursor: pointer;">✕</button>
                </div>
                <div id="btu-chat-messages" style="flex: 1; padding: 12px; overflow-y: auto; background-color: #f9f9f9; display: flex; flex-direction: column; gap: 10px;">
                    <div style="background-color: #e9ecef; padding: 10px; border-radius: 8px; font-size: 13px; color: #333;">
                        Merhaba! Ben BTÜ Asistan. Bursa Teknik Üniversitesi hakkındaki tüm sorularını bana sorabilirsin! 🎓
                    </div>
                </div>
                <div style="padding: 10px; border-top: 1px solid #eee; display: flex; gap: 8px; background-color: white;">
                    <input type="text" id="btu-chat-input" placeholder="Sorunuzu yazın..." style="flex: 1; padding: 8px 12px; border: 1px solid #ccc; border-radius: 20px; outline: none; font-size: 13px;">
                    <button id="btu-chat-send" style="background-color: #0d3b66; color: white; border: none; border-radius: 50%; width: 36px; height: 36px; cursor: pointer; font-size: 14px;">➤</button>
                </div>
            </div>
        </div>
    `;

    document.body.insertAdjacentHTML('beforeend', WIDGET_HTML);

    const toggleBtn = document.getElementById('btu-widget-toggle');
    const closeBtn = document.getElementById('btu-chat-close');
    const chatBox = document.getElementById('btu-chat-box');
    const sendBtn = document.getElementById('btu-chat-send');
    const chatInput = document.getElementById('btu-chat-input');
    const messagesDiv = document.getElementById('btu-chat-messages');

    toggleBtn.addEventListener('click', () => {
        chatBox.style.display = chatBox.style.display === 'none' ? 'flex' : 'none';
    });

    closeBtn.addEventListener('click', () => {
        chatBox.style.display = 'none';
    });

    async function sendMessage() {
        const text = chatInput.value.trim();
        if (!text) return;

        // Render User Message
        const userMsg = document.createElement('div');
        userMsg.style.cssText = "background-color: #0d3b66; color: white; padding: 8px 12px; border-radius: 12px; align-self: flex-end; font-size: 13px; max-width: 80%;";
        userMsg.innerText = text;
        messagesDiv.appendChild(userMsg);
        chatInput.value = '';
        messagesDiv.scrollTop = messagesDiv.scrollHeight;

        // Render Loading Indicator
        const loadingMsg = document.createElement('div');
        loadingMsg.style.cssText = "background-color: #e9ecef; color: #666; padding: 8px 12px; border-radius: 12px; align-self: flex-start; font-size: 12px;";
        loadingMsg.innerText = "BTÜ Asistan yazıyor...";
        messagesDiv.appendChild(loadingMsg);
        messagesDiv.scrollTop = messagesDiv.scrollHeight;

        try {
            const response = await fetch('http://localhost:8000/api/ask', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ question: text })
            });
            const data = await response.json();
            messagesDiv.removeChild(loadingMsg);

            const botMsg = document.createElement('div');
            botMsg.style.cssText = "background-color: #e9ecef; color: #333; padding: 10px; border-radius: 12px; align-self: flex-start; font-size: 13px; max-width: 85%; white-space: pre-wrap;";
            botMsg.innerText = data.answer || "Cevap üretilemedi.";
            messagesDiv.appendChild(botMsg);
        } catch (err) {
            messagesDiv.removeChild(loadingMsg);
            const errMsg = document.createElement('div');
            errMsg.style.cssText = "background-color: #f8d7da; color: #721c24; padding: 8px 12px; border-radius: 12px; align-self: flex-start; font-size: 12px;";
            errMsg.innerText = "API servisine bağlanılamadı. Lütfen api.py'ın çalıştığından emin olun.";
            messagesDiv.appendChild(errMsg);
        }
        messagesDiv.scrollTop = messagesDiv.scrollHeight;
    }

    sendBtn.addEventListener('click', sendMessage);
    chatInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') sendMessage();
    });
})();
