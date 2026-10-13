(function () {
    "use strict";

    function ready(fn) {
        if (document.readyState !== "loading") fn();
        else document.addEventListener("DOMContentLoaded", fn);
    }

    function showToast(message, kind) {
        var stack = document.querySelector(".toast-stack");
        if (!stack) {
            stack = document.createElement("div");
            stack.className = "toast-stack";
            stack.setAttribute("aria-live", "polite");
            document.body.appendChild(stack);
        }
        var item = document.createElement("div");
        item.className = "toast-item " + (kind || "info");
        item.setAttribute("role", kind === "error" ? "alert" : "status");

        var icon = document.createElement("i");
        icon.setAttribute("aria-hidden", "true");
        icon.className = "bi " + (kind === "success"
            ? "bi-check-circle-fill text-success"
            : kind === "warning"
                ? "bi-exclamation-triangle-fill text-warning"
                : "bi-exclamation-octagon-fill text-danger");

        var text = document.createElement("span");
        text.textContent = message;

        var close = document.createElement("button");
        close.type = "button";
        close.className = "toast-close";
        close.setAttribute("aria-label", "Dismiss");
        close.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
        close.addEventListener("click", function () { item.remove(); });

        item.appendChild(icon);
        item.appendChild(text);
        item.appendChild(close);
        stack.appendChild(item);
        window.setTimeout(function () { item.remove(); }, 8000);
    }

    ready(function () {
        var toggle = document.querySelector("[data-nav-toggle]");
        var sidebar = document.querySelector(".sidebar");
        var backdrop = document.querySelector(".sidebar-backdrop");
        if (toggle && sidebar) {
            toggle.addEventListener("click", function () {
                sidebar.classList.toggle("open");
                if (backdrop) backdrop.classList.toggle("show");
            });
        }
        if (backdrop) {
            backdrop.addEventListener("click", function () {
                sidebar.classList.remove("open");
                backdrop.classList.remove("show");
            });
        }

        document.querySelectorAll(".toast-item").forEach(function (el) {
            var close = el.querySelector(".toast-close");
            if (close) close.addEventListener("click", function () { el.remove(); });
            var timeout = parseInt(el.getAttribute("data-timeout") || "8000", 10);
            if (timeout > 0) setTimeout(function () { el.remove(); }, timeout);
        });

        document.querySelectorAll("[data-history-back]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                if (window.history.length > 1) window.history.back();
                else window.location.href = "/";
            });
        });

        document.querySelectorAll("[data-reload]").forEach(function (btn) {
            btn.addEventListener("click", function () { window.location.reload(); });
        });

        // Confirmations and duplicate-submission protection.
        document.addEventListener("submit", function (e) {
            var form = e.target;
            if (!(form instanceof HTMLFormElement)) return;

            var message = (e.submitter && e.submitter.getAttribute("data-confirm")) || form.getAttribute("data-confirm");
            var uncheckedName = form.getAttribute("data-confirm-unchecked");
            if (!message && uncheckedName) {
                var box = form.querySelector('input[name="' + uncheckedName + '"]');
                if (box && box.type === "checkbox" && !box.checked) {
                    message = form.getAttribute("data-confirm-unchecked-message");
                }
            }
            if (message && !window.confirm(message)) {
                e.preventDefault();
                return;
            }

            // Runs after client-side validation has had its say; keeps dimensions stable.
            window.setTimeout(function () {
                if (e.defaultPrevented) return;
                form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(function (button) {
                    if (button.disabled) return;
                    button.disabled = true;
                    button.setAttribute("aria-busy", "true");
                    var spinner = document.createElement("span");
                    spinner.className = "spinner";
                    spinner.setAttribute("aria-hidden", "true");
                    button.insertBefore(spinner, button.firstChild);
                });
            }, 0);
        });

        if (window.Chart) {
            document.querySelectorAll("canvas[data-chart]").forEach(function (canvas) {
                var raw = canvas.getAttribute("data-chart-values");
                var rawLabels = canvas.getAttribute("data-chart-labels");
                if (!raw) return;
                var values, labels;
                try { values = JSON.parse(raw); } catch (e) { return; }
                try { labels = JSON.parse(rawLabels || "[]"); } catch (e) { labels = []; }
                var type = canvas.getAttribute("data-chart") || "bar";
                // DESIGN.md §24.1 chart series colors.
                var palette = ["#2563EB", "#0F766E", "#7C3AED", "#D97706", "#667085"];
                new Chart(canvas, {
                    type: type,
                    data: {
                        labels: labels,
                        datasets: [{
                            data: values,
                            backgroundColor: type === "doughnut" ? palette : "#2563EB",
                            borderColor: "#FFFFFF",
                            borderWidth: type === "doughnut" ? 2 : 0,
                            borderRadius: type === "bar" ? 4 : 0
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        font: { family: "Inter, system-ui, sans-serif" },
                        plugins: {
                            legend: {
                                display: type === "doughnut",
                                position: "bottom",
                                labels: { boxWidth: 12, boxHeight: 12, font: { family: "Inter, system-ui, sans-serif", size: 12 } }
                            }
                        },
                        scales: type === "doughnut" ? {} : {
                            y: { beginAtZero: true, ticks: { precision: 0, font: { family: "Inter, system-ui, sans-serif", size: 12 } }, grid: { color: "#E3E8EF" } },
                            x: { grid: { display: false }, ticks: { font: { family: "Inter, system-ui, sans-serif", size: 12 } } }
                        }
                    }
                });
            });
        }

        document.querySelectorAll("[data-auto-submit]").forEach(function (el) {
            el.addEventListener("change", function () {
                var form = el.closest("form");
                if (form) form.submit();
            });
        });

        document.querySelectorAll("[data-clock-action]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                var action = btn.getAttribute("data-clock-action");
                btn.disabled = true;
                btn.setAttribute("aria-busy", "true");
                var spinner = document.createElement("span");
                spinner.className = "spinner";
                spinner.setAttribute("aria-hidden", "true");
                btn.insertBefore(spinner, btn.firstChild);

                var token = document.querySelector('input[name="__RequestVerificationToken"]');
                fetch("/Attendance/" + action, {
                    method: "POST",
                    headers: {
                        "RequestVerificationToken": token ? token.value : "",
                        "X-Requested-With": "XMLHttpRequest"
                    }
                }).then(function (r) { return r.json().catch(function () { return { success: r.ok }; }); })
                    .then(function (data) {
                        if (data.success) {
                            window.location.reload();
                        } else {
                            spinner.remove();
                            btn.disabled = false;
                            btn.removeAttribute("aria-busy");
                            showToast(data.message || "The clock " + (action === "ClockIn" ? "in" : "out") + " could not be recorded. Try again.", "error");
                        }
                    })
                    .catch(function () {
                        spinner.remove();
                        btn.disabled = false;
                        btn.removeAttribute("aria-busy");
                        showToast("Could not reach the server to record your clock " + (action === "ClockIn" ? "in" : "out") + ". Check your connection and try again.", "error");
                    });
            });
        });
    });
})();
