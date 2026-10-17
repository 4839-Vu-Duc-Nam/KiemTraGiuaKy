const state = {
  activeAttempt: null,
  deadline: null,
  exam: null,
  saveTimer: null,
  timer: null
};

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];

async function api(path, options = {}) {
  const response = await fetch(`/api${path}`, options);
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    const message = body?.error || body?.title
      || body?.errors?.map(item => `Dòng ${item.line}: ${item.message}`).join("\n")
      || `Yêu cầu thất bại (${response.status}).`;
    throw new Error(message);
  }
  return response.status === 204 ? null : response.json();
}

function showMessage(element, text, kind = "success") {
  element.className = `message ${kind}`;
  element.textContent = text;
}

function setView(name) {
  $$(".tab").forEach(tab => tab.classList.toggle("active", tab.dataset.view === name));
  $("#admin-view").classList.toggle("hidden", name !== "admin");
  $("#learner-view").classList.toggle("hidden", name !== "learner");
  if (name === "learner") loadPublishedExams();
}

$$(".tab").forEach(tab => tab.addEventListener("click", () => setView(tab.dataset.view)));

$("#docx-file").addEventListener("change", event => {
  $("#file-name").textContent = event.target.files[0]?.name || "hoặc kéo file vào đây";
});

$("#upload-form").addEventListener("submit", async event => {
  event.preventDefault();
  const uploadForm = event.currentTarget;
  const message = $("#import-message");
  const button = $("button[type=submit]", uploadForm);
  const file = $("#docx-file").files[0];
  if (!file) return;
  button.disabled = true;
  message.textContent = "";
  try {
    const form = new FormData();
    form.append("file", file);
    const result = await api("/exams/import", { method: "POST", body: form });
    showMessage(message, `Đã import "${result.title}" (${result.questionCount} câu). Đề đang ở trạng thái nháp.`);
    uploadForm.reset();
    $("#file-name").textContent = "hoặc kéo file vào đây";
    await loadAdminExams();
  } catch (error) {
    message.className = "message error";
    message.textContent = error.message;
    if (error.message.includes("\n")) {
      const list = document.createElement("ul");
      list.className = "error-list";
      error.message.split("\n").forEach(item => {
        const line = document.createElement("li");
        line.textContent = item;
        list.append(line);
      });
      message.replaceChildren(list);
    }
  } finally {
    button.disabled = false;
  }
});

$("#refresh-exams").addEventListener("click", loadAdminExams);

async function loadAdminExams() {
  const list = $("#exam-list");
  try {
    const exams = await api("/exams?includeDrafts=true");
    if (exams.length === 0) {
      list.innerHTML = '<p class="muted">Chưa có đề thi. Hãy import một file DOCX hợp lệ.</p>';
      return;
    }
    list.replaceChildren(...exams.map(exam => {
      const row = document.createElement("article");
      row.className = "exam-row";
      row.innerHTML = `<div class="exam-emblem">EX</div><div class="exam-row-main">
        <h3 title="${escapeHtml(exam.title)}">${escapeHtml(exam.title)}</h3>
        <div class="exam-meta">${exam.questionCount} câu · ${exam.durationMinutes} phút ·
          <span class="publish-state ${exam.isPublished ? "" : "draft"}">${exam.isPublished ? "Đã xuất bản" : "Bản nháp"}</span></div>
      </div><div class="row-actions">
        <button class="button ghost" data-preview="${exam.id}">Xem trước</button>
        <button class="button ghost" data-stats="${exam.id}">Thống kê</button>
        ${exam.isPublished ? "" : `<button class="button primary" data-publish="${exam.id}" ${exam.questionCount === 100 ? "" : "disabled"}>Xuất bản</button>`}
      </div>`;
      return row;
    }));
    $$("[data-preview]", list).forEach(button => button.addEventListener("click", () => showPreview(button.dataset.preview)));
    $$("[data-stats]", list).forEach(button => button.addEventListener("click", () => showStats(button.dataset.stats)));
    $$("[data-publish]", list).forEach(button => button.addEventListener("click", () => publishExam(button.dataset.publish, button)));
  } catch (error) {
    list.innerHTML = `<p class="message error">${escapeHtml(error.message)}</p>`;
  }
}

async function publishExam(id, button) {
  button.disabled = true;
  try {
    await api(`/exams/${id}/publish`, { method: "POST" });
    await loadAdminExams();
  } catch (error) {
    button.disabled = false;
    alert(error.message);
  }
}

async function showPreview(id) {
  const panel = $("#admin-detail");
  panel.classList.remove("hidden");
  panel.innerHTML = '<p class="muted">Đang tải bản xem trước...</p>';
  try {
    const exam = await api(`/exams/${id}?preview=true`);
    panel.innerHTML = `<div class="panel-title"><div><span class="step">✓</span><h2>${escapeHtml(exam.title)}</h2></div>
      <span class="badge">${exam.questionCount} câu · ${exam.durationMinutes} phút</span></div>
      <p class="muted">Đề nháp có thể chỉnh sửa trước khi xuất bản.</p><div id="preview-questions"></div>`;
    const container = $("#preview-questions", panel);
    const renderedPassages = new Set();
    exam.questions.forEach((question, index) => {
      if (question.passageId !== null && !renderedPassages.has(question.passageId)) {
        renderedPassages.add(question.passageId);
        const passage = document.createElement("form");
        passage.className = "question-card edit-passage";
        passage.innerHTML = `<label class="field-label">Nội dung Part ${question.part}</label>
          <textarea class="text-input passage-input" rows="5" required>${escapeHtml(question.passageContent || "")}</textarea>
          <button class="button ghost small-button" type="submit">Lưu đoạn đọc</button>`;
        passage.addEventListener("submit", event => savePassage(event, id, question.passageId));
        container.append(passage);
      }
      const card = document.createElement("article");
      card.className = "question-card";
      card.innerHTML = `<form class="edit-question" data-question="${question.id}">
        <div class="question-line"><span class="question-number">${index + 1}</span>
          <div style="flex:1"><div class="exam-meta">Part ${question.part}</div>
            <label class="field-label" for="prompt-${question.id}">Nội dung câu hỏi</label>
            <textarea id="prompt-${question.id}" class="text-input edit-prompt" rows="2" required>${escapeHtml(question.prompt)}</textarea>
            <div class="answer-list edit-options">${question.options.map(option => `<label class="answer-option">
              <input type="radio" name="correct-${question.id}" value="${escapeHtml(option.letter)}" ${option.letter === question.correctLetter ? "checked" : ""}>
              <span><strong>${escapeHtml(option.letter)}.</strong>
                <input class="text-input option-input" data-letter="${escapeHtml(option.letter)}" value="${escapeHtml(option.text)}" required></span></label>`).join("")}</div>
            <label class="field-label" for="hint-${question.id}">Gợi ý câu trả lời</label>
            <textarea id="hint-${question.id}" class="text-input edit-hint" rows="2" required>${escapeHtml(question.hint || "")}</textarea>
            <button class="button ghost small-button" type="submit">Lưu câu hỏi</button>
          </div>
        </div></form>`;
      $(".edit-question", card).addEventListener("submit", event => saveQuestion(event, id));
      container.append(card);
    });
  } catch (error) {
    panel.innerHTML = `<p class="message error">${escapeHtml(error.message)}</p>`;
  }
}

async function saveQuestion(event, examId) {
  event.preventDefault();
  const form = event.currentTarget;
  const questionId = form.dataset.question;
  const correctLetter = $(`input[name="correct-${questionId}"]:checked`, form)?.value;
  const options = $$(".option-input", form).map(input => ({ letter: input.dataset.letter, text: input.value }));
  try {
    await api(`/exams/${examId}/questions/${questionId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        prompt: $(".edit-prompt", form).value,
        hint: $(".edit-hint", form).value,
        options,
        correctLetter
      })
    });
    showMessageFor(form, "Đã lưu nội dung câu hỏi.");
  } catch (error) {
    showMessageFor(form, error.message, true);
  }
}

async function savePassage(event, examId, passageId) {
  event.preventDefault();
  const form = event.currentTarget;
  try {
    await api(`/exams/${examId}/passages/${passageId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ content: $(".passage-input", form).value })
    });
    showMessageFor(form, "Đã lưu nội dung đoạn đọc.");
  } catch (error) {
    showMessageFor(form, error.message, true);
  }
}

function showMessageFor(form, text, error = false) {
  let message = $(".message", form);
  if (!message) {
    message = document.createElement("div");
    form.append(message);
  }
  message.className = `message ${error ? "error" : "success"}`;
  message.textContent = text;
}

async function showStats(id) {
  const panel = $("#admin-detail");
  panel.classList.remove("hidden");
  panel.innerHTML = '<p class="muted">Đang tổng hợp kết quả...</p>';
  try {
    const stats = await api(`/exams/${id}/stats`);
    panel.innerHTML = `<div class="panel-title"><div><span class="step">03</span><h2>Thống kê kết quả</h2></div>
      <span class="badge">${stats.participants} lượt thi</span></div>
      <div class="stats-grid">
        ${metric(stats.participants, "Thí sinh tham gia")}
        ${metric(`${stats.averageScorePercent}%`, "Điểm trung bình")}
        ${metric(stats.aboveAverage, "Trên trung bình")}
        ${metric(stats.belowAverage, "Dưới trung bình")}
      </div>
      <div class="chart-wrap"><canvas id="score-chart" aria-label="Biểu đồ so sánh điểm"></canvas></div>
      <h3>10 câu hỏi khó nhất</h3>
      <ol class="hardest-list">${stats.hardestQuestions.length
        ? stats.hardestQuestions.map(question => `<li>Part ${question.part} · ${question.incorrectPercent}% sai — ${escapeHtml(question.prompt)}</li>`).join("")
        : "<li>Chưa có bài thi đã nộp.</li>"}</ol>`;
    drawStatsChart($("#score-chart", panel), stats);
  } catch (error) {
    panel.innerHTML = `<p class="message error">${escapeHtml(error.message)}</p>`;
  }
}

function metric(value, label) {
  return `<div class="metric"><strong>${escapeHtml(value)}</strong><span>${escapeHtml(label)}</span></div>`;
}

function drawStatsChart(canvas, stats) {
  const context = canvas.getContext("2d");
  const ratio = window.devicePixelRatio || 1;
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  canvas.width = width * ratio;
  canvas.height = height * ratio;
  context.scale(ratio, ratio);
  const bars = [
    { name: "Trên TB", value: stats.aboveAverage, color: "#40b893" },
    { name: "Bằng TB", value: stats.equalToAverage, color: "#74b6b0" },
    { name: "Dưới TB", value: stats.belowAverage, color: "#e08a88" }
  ];
  const max = Math.max(1, ...bars.map(bar => bar.value));
  const slot = width / bars.length;
  bars.forEach((bar, index) => {
    const barWidth = Math.min(65, slot * .34);
    const barHeight = stats.participants === 0 ? 0 : (bar.value / max) * 105;
    const x = slot * index + (slot - barWidth) / 2;
    context.fillStyle = bar.color;
    context.beginPath();
    context.roundRect(x, 123 - barHeight, barWidth, barHeight, 6);
    context.fill();
    context.fillStyle = "#526873";
    context.font = "11px DM Sans, sans-serif";
    context.textAlign = "center";
    context.fillText(`${bar.value}`, x + barWidth / 2, 114 - barHeight);
    context.fillText(bar.name, x + barWidth / 2, 150);
  });
}

async function loadPublishedExams() {
  const container = $("#published-exams");
  try {
    const exams = await api("/exams");
    if (exams.length === 0) {
      container.innerHTML = '<p class="muted">Chưa có đề thi được xuất bản.</p>';
      return;
    }
    container.replaceChildren(...exams.map(exam => {
      const card = document.createElement("article");
      card.className = "exam-card";
      card.innerHTML = `<div class="exam-emblem">TOEIC</div><h3>${escapeHtml(exam.title)}</h3>
        <span class="exam-meta">${exam.questionCount} câu hỏi · ${exam.durationMinutes} phút</span>
        <button class="button primary" data-start="${exam.id}">Bắt đầu làm bài</button>`;
      return card;
    }));
    $$("[data-start]", container).forEach(button => button.addEventListener("click", () => startAttempt(button.dataset.start, button)));
  } catch (error) {
    container.innerHTML = `<p class="message error">${escapeHtml(error.message)}</p>`;
  }
}

async function startAttempt(examId, button) {
  const name = $("#learner-name").value.trim();
  if (!name) {
    $("#learner-name").focus();
    alert("Vui lòng nhập họ tên trước khi bắt đầu.");
    return;
  }
  button.disabled = true;
  try {
    const attempt = await api(`/exams/${examId}/attempts`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ learnerName: name })
    });
    state.activeAttempt = attempt.attemptId;
    state.deadline = attempt.deadline;
    state.exam = attempt.exam;
    renderTest(attempt.exam);
    startTimer();
  } catch (error) {
    button.disabled = false;
    alert(error.message);
  }
}

function renderTest(exam) {
  $("#learner-home").classList.add("hidden");
  $("#result-screen").classList.add("hidden");
  const screen = $("#test-screen");
  screen.classList.remove("hidden");
  screen.innerHTML = `<div class="test-heading"><div><h2>${escapeHtml(exam.title)}</h2>
      <span class="exam-meta">Đang làm bài · Đã lưu tự động</span></div><div id="timer" class="timer">75:00</div></div>
    <div class="test-content" id="question-list"></div>
    <div class="test-actions"><span id="save-state" class="save-state">Câu trả lời được lưu sau mỗi thay đổi.</span>
      <button id="submit-test" class="button primary">Nộp bài</button></div>`;
  const container = $("#question-list", screen);
  let lastPassageId = null;
  exam.questions.forEach((question, index) => {
    if (question.passageId !== null && question.passageId !== lastPassageId) {
      const passage = document.createElement("article");
      passage.className = "passage-box";
      passage.textContent = question.passageContent || "";
      container.append(passage);
    }
    lastPassageId = question.passageId;
    const card = document.createElement("article");
    card.className = "question-card";
    card.innerHTML = `<div class="question-line"><span class="question-number">${index + 1}</span>
      <div style="flex:1"><p class="question-prompt">${escapeHtml(question.prompt)}</p>
        <div class="answer-list">${question.options.map(option => `<label class="answer-option">
          <input type="radio" name="question-${question.id}" value="${escapeHtml(option.letter)}"
            ${option.letter === question.selectedLetter ? "checked" : ""}>
          <span><strong>${escapeHtml(option.letter)}.</strong> ${escapeHtml(option.text)}</span></label>`).join("")}</div>
      </div></div>`;
    container.append(card);
  });
  $$("input[type=radio]", container).forEach(input => input.addEventListener("change", scheduleSave));
  $("#submit-test").addEventListener("click", () => submitAttempt(false));
}

function scheduleSave() {
  $("#save-state").textContent = "Đang lưu câu trả lời...";
  clearTimeout(state.saveTimer);
  state.saveTimer = setTimeout(saveAnswers, 450);
}

async function saveAnswers() {
  if (!state.activeAttempt) return;
  const answers = state.exam.questions.map(question => ({
    questionId: question.id,
    selectedLetter: $(`input[name="question-${question.id}"]:checked`)?.value || null
  }));
  try {
    await api(`/attempts/${state.activeAttempt}/answers`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ answers })
    });
    $("#save-state").textContent = `Đã lưu lúc ${new Date().toLocaleTimeString("vi-VN")}`;
  } catch (error) {
    $("#save-state").textContent = `Lỗi lưu bài: ${error.message}`;
    $("#save-state").classList.add("message", "error");
  }
}

function startTimer() {
  clearInterval(state.timer);
  const tick = () => {
    const remaining = Math.max(0, Math.ceil((new Date(state.deadline).getTime() - Date.now()) / 1000));
    const timer = $("#timer");
    if (!timer) return;
    timer.textContent = `${String(Math.floor(remaining / 60)).padStart(2, "0")}:${String(remaining % 60).padStart(2, "0")}`;
    timer.classList.toggle("warning", remaining <= 300);
    if (remaining === 0) {
      clearInterval(state.timer);
      submitAttempt(true);
    }
  };
  tick();
  state.timer = setInterval(tick, 1000);
}

async function submitAttempt(autoSubmit) {
  clearTimeout(state.saveTimer);
  if (!autoSubmit) {
    const unanswered = state.exam.questions.filter(question => !$(`input[name="question-${question.id}"]:checked`)).length;
    const warning = unanswered ? `Bạn còn ${unanswered} câu chưa trả lời. ` : "";
    if (!confirm(`${warning}Bạn chắc chắn muốn nộp bài? Sau khi nộp sẽ không thể sửa đáp án.`)) return;
  }
  const button = $("#submit-test");
  if (button) button.disabled = true;
  clearInterval(state.timer);
  try {
    await saveAnswers();
    const result = await api(`/attempts/${state.activeAttempt}/submit`, { method: "POST" });
    state.activeAttempt = null;
    renderResult(result);
  } catch (error) {
    if (button) button.disabled = false;
    alert(error.message);
    startTimer();
  }
}

function renderResult(result) {
  $("#test-screen").classList.add("hidden");
  const screen = $("#result-screen");
  screen.classList.remove("hidden");
  screen.innerHTML = `<div class="result-header"><p class="eyebrow">KẾT QUẢ BÀI THI</p>
      <h2>${escapeHtml(result.examTitle)}</h2><p>${escapeHtml(result.learnerName)} · ${new Date(result.submittedAt).toLocaleString("vi-VN")}</p></div>
    <div class="result-metrics">
      <div class="metric"><strong>${result.scorePercent}%</strong><span>Điểm số</span></div>
      <div class="metric"><strong>${result.correct}</strong><span>Câu đúng</span></div>
      <div class="metric"><strong>${result.incorrect}</strong><span>Câu sai</span></div>
      <div class="metric"><strong>${result.blank}</strong><span>Bỏ qua</span></div>
    </div><h3>Đáp án chi tiết</h3><div id="result-questions"></div>
    <button id="back-to-exams" class="button primary" style="margin-top:14px">Về danh sách đề thi</button>`;
  const list = $("#result-questions", screen);
  result.questions.forEach((question, index) => {
    const article = document.createElement("article");
    article.className = `result-question ${question.isCorrect ? "correct" : "incorrect"}`;
    article.innerHTML = `<strong>${index + 1}. Part ${question.part} · ${escapeHtml(question.prompt)}</strong>
      <p>Câu trả lời: ${escapeHtml(question.selectedLetter || "Bỏ qua")} · Đáp án đúng: ${escapeHtml(question.correctLetter)}</p>`;
    list.append(article);
  });
  $("#back-to-exams").addEventListener("click", () => {
    screen.classList.add("hidden");
    $("#learner-home").classList.remove("hidden");
    loadPublishedExams();
  });
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, character => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  })[character]);
}

loadAdminExams();
