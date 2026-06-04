document.addEventListener("click", (event) => {
  const summary = event.target.closest("summary");
  if (!summary) return;

  document.querySelectorAll("details[open]").forEach((details) => {
    if (details !== summary.parentElement) {
      details.removeAttribute("open");
    }
  });
});

