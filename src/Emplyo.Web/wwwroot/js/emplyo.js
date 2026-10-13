(function () {
    "use strict";

    function ready(fn) {
        if (document.readyState !== "loading") fn();
        else document.addEventListener("DOMContentLoaded", fn);
    }

    ready(function () {
        // Sidebar toggle (mobile)
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

        // Toasts
        document.querySelectorAll(".toast-item").forEach(function (el) {
            var close = el.querySelector(".toast-close");
            if (close) close.addEventListener("click", function () { el.remove(); });
            var timeout = parseInt(el.getAttribute("data-timeout") || "6000", 10);
            if (timeout > 0) setTimeout(function () { el.remove(); }, timeout);
        });

        // Confirmation dialogs
        document.querySelectorAll("form[data-confirm]").forEach(function (form) {
            form.addEventListener("submit", function (e) {
                if (!window.confirm(form.getAttribute("data-confirm"))) e.preventDefault();
            });
        });

        // Element-based chart renderer (Chart.js)
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

        // Filter form auto-submit on select change
        document.querySelectorAll("[data-auto-submit]").forEach(function (el) {
            el.addEventListener("change", function () {
                var form = el.closest("form");
                if (form) form.submit();
            });
        });

        // Clock in/out
        document.querySelectorAll("[data-clock-action]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                var action = btn.getAttribute("data-clock-action");
                btn.disabled = true;
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
                            window.alert(data.message || "The operation could not be completed.");
                            btn.disabled = false;
                        }
                    })
                    .catch(function () { btn.disabled = false; window.alert("Network error."); });
            });
        });
    });
})();
