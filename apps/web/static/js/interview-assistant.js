(() => {
  const room = document.querySelector("[data-session-id]");
  if (!room) return;

  const sessionId = room.dataset.sessionId;
  const transcriptBox = document.getElementById("transcriptBox");
  const questionBox = document.getElementById("questionBox");
  const answerMode = document.getElementById("answerMode");
  const answerSurface = document.getElementById("answerSurface");
  const speechStatus = document.getElementById("speechStatus");
  const startButton = document.getElementById("startListening");
  const stopButton = document.getElementById("stopListening");
  const clearButton = document.getElementById("clearTranscript");
  const askButton = document.getElementById("askAssistant");

  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  let recognition = null;
  let listening = false;

  function setStatus(text) {
    if (speechStatus) speechStatus.textContent = text;
  }

  function appendTranscript(text) {
    if (!text) return;
    const prefix = transcriptBox.value.trim() ? "\n" : "";
    transcriptBox.value += `${prefix}${text.trim()}`;
    transcriptBox.scrollTop = transcriptBox.scrollHeight;
  }

  if (SpeechRecognition) {
    recognition = new SpeechRecognition();
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.lang = "en-US";

    recognition.onstart = () => {
      listening = true;
      setStatus("Listening. Browser microphone permission is active.");
    };
    recognition.onend = () => {
      listening = false;
      setStatus("Mic stopped. You can restart it or keep typing manually.");
    };
    recognition.onerror = (event) => {
      listening = false;
      setStatus(`Speech capture stopped: ${event.error || "browser error"}. Manual typing still works.`);
    };
    recognition.onresult = (event) => {
      let finalText = "";
      for (let index = event.resultIndex; index < event.results.length; index += 1) {
        const result = event.results[index];
        if (result.isFinal && result[0]) finalText += `${result[0].transcript} `;
      }
      appendTranscript(finalText);
    };
  } else {
    setStatus("This browser does not expose speech capture here. Type or paste the transcript manually.");
    if (startButton) startButton.disabled = true;
    if (stopButton) stopButton.disabled = true;
  }

  if (startButton) {
    startButton.addEventListener("click", () => {
      if (!recognition || listening) return;
      try {
        recognition.start();
      } catch {
        setStatus("Mic is already starting. Manual typing still works.");
      }
    });
  }

  if (stopButton) {
    stopButton.addEventListener("click", () => {
      if (recognition && listening) recognition.stop();
    });
  }

  if (clearButton) {
    clearButton.addEventListener("click", () => {
      transcriptBox.value = "";
      questionBox.value = "";
      setStatus("Transcript cleared.");
    });
  }

  if (askButton) {
    askButton.addEventListener("click", async () => {
      const transcript = transcriptBox.value.trim();
      const question = questionBox.value.trim();
      if (!transcript && !question) {
        answerSurface.innerHTML = "<span class=\"eyebrow\">Need input</span><p>Add a question or transcript first.</p>";
        return;
      }
      askButton.disabled = true;
      answerSurface.innerHTML = "<span class=\"eyebrow\">Thinking</span><p>Preparing a coachable answer...</p>";
      try {
        const response = await fetch(`/career/interview-assistant/sessions/${sessionId}/ask`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            transcript,
            question,
            mode: answerMode.value,
          }),
        });
        const payload = await response.json();
        if (!response.ok) {
          throw new Error(payload.error || "Interview Assistant could not answer.");
        }
        const engine = payload.used_ai ? "AI answer" : "Local coaching";
        const safeAnswer = String(payload.answer || "").replace(/[&<>"']/g, (char) => ({
          "&": "&amp;",
          "<": "&lt;",
          ">": "&gt;",
          "\"": "&quot;",
          "'": "&#039;",
        }[char]));
        answerSurface.innerHTML = `<span class="eyebrow">${engine}</span><pre>${safeAnswer}</pre>`;
      } catch (error) {
        answerSurface.innerHTML = `<span class="eyebrow">Error</span><p>${error.message}</p>`;
      } finally {
        askButton.disabled = false;
      }
    });
  }
})();
