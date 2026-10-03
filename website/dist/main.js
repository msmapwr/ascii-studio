// Keep enhancement independent of the component CDN so content and navigation survive a failed import.
const root = document.documentElement;
const systemTheme = matchMedia("(prefers-color-scheme: dark)");
const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
const themeNames = { system: "跟随系统", light: "浅色", dark: "深色" };
let preference = "system";
try {
    const saved = localStorage.getItem("asciistudio-theme");
    if (Object.hasOwn(themeNames, saved)) preference = saved;
} catch {
    /* Storage can be unavailable in private or embedded browsing. */
}
let fluent;
let fluentThemes;
function applyTheme(announce = false) {
    const resolved =
        preference === "system"
            ? systemTheme.matches
                ? "dark"
                : "light"
            : preference;
    root.dataset.theme = resolved;
    if (fluent && fluentThemes) fluent.setTheme(fluentThemes[resolved]);
    if (announce)
        document.querySelector("#theme-status").textContent =
            preference === "system"
                ? "外观已设为跟随系统。"
                : `已切换为${themeNames[preference]}外观。`;
}
applyTheme();
systemTheme.addEventListener("change", () => {
    if (preference === "system") applyTheme();
});

const menuButton = /** @type {HTMLButtonElement} */ (
    document.querySelector("#menu-toggle")
);
const navigation = /** @type {HTMLElement} */ (
    document.querySelector("#navigation")
);
const compactScreen = matchMedia("(max-width: 1000px)");
function closeMenu() {
    navigation.hidden = compactScreen.matches;
    menuButton.setAttribute("aria-expanded", "false");
    menuButton.setAttribute("aria-label", "打开导航");
}
root.classList.add("menu-ready");
closeMenu();
compactScreen.addEventListener("change", closeMenu);
menuButton.addEventListener("click", () => {
    const opening = menuButton.getAttribute("aria-expanded") !== "true";
    navigation.hidden = !opening;
    menuButton.setAttribute("aria-expanded", String(opening));
    menuButton.setAttribute("aria-label", opening ? "关闭导航" : "打开导航");
});
navigation.addEventListener("click", (event) => {
    if (
        event.target instanceof Element &&
        event.target.closest("a") &&
        compactScreen.matches
    )
        closeMenu();
});
document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
        const resources = /** @type {HTMLDetailsElement} */ (
            document.querySelector("#resource-menu")
        );
        if (resources?.open) {
            resources.open = false;
            resources.querySelector("summary").focus();
            return;
        }
        if (
            compactScreen.matches &&
            menuButton.getAttribute("aria-expanded") === "true"
        ) {
            closeMenu();
            menuButton.focus();
        }
        const picker = /** @type {HTMLDetailsElement} */ (
            document.querySelector("#theme-picker")
        );
        if (picker.open) {
            picker.open = false;
            picker.querySelector("summary").focus();
        }
    }
});
document.addEventListener("click", (event) => {
    const resources = /** @type {HTMLDetailsElement} */ (
        document.querySelector("#resource-menu")
    );
    if (resources && !resources.contains(/** @type {Node} */ (event.target)))
        resources.open = false;
    const picker = /** @type {HTMLDetailsElement} */ (
        document.querySelector("#theme-picker")
    );
    if (!picker.contains(/** @type {Node} */ (event.target)))
        picker.open = false;
});

function initDemo() {
    const demoImage = /** @type {HTMLImageElement} */ (
        document.querySelector("#demo-image")
    );
    const demoButton = /** @type {HTMLButtonElement} */ (
        document.querySelector("#demo-toggle")
    );
    const demoCaption = document.querySelector("#demo-caption");
    if (!demoImage || !demoButton || !demoCaption) return;
    let playing = false;
    let demoVisible = false;
    let manuallyPaused = false;
    function updateDemo() {
        if (reducedMotion.matches) playing = false;
        demoImage.src = playing
            ? "assets/text-workflow.gif"
            : "assets/text-result.png";
        demoImage.alt = playing
            ? "AsciiStudio 中输入文字、生成字符画并切换内容的操作过程。"
            : "AsciiStudio 生成的文字字符画静态结果。";
        demoCaption.textContent = playing
            ? "文字转换的实际操作过程。"
            : "文字转换结果。";
        demoButton.hidden = reducedMotion.matches;
        demoButton.textContent = playing ? "停止演示" : "播放操作演示";
        demoButton.setAttribute("aria-pressed", String(playing));
    }
    demoButton.hidden = false;
    updateDemo();
    demoButton.addEventListener("click", () => {
        playing = !playing;
        manuallyPaused = !playing;
        updateDemo();
    });
    function syncDemoPlayback() {
        playing = demoVisible && !manuallyPaused && !reducedMotion.matches;
        updateDemo();
    }
    if ("IntersectionObserver" in window) {
        const observer = new IntersectionObserver(
            (entries) => {
                demoVisible = entries[0].isIntersecting;
                if (!demoVisible) manuallyPaused = false;
                syncDemoPlayback();
            },
            { threshold: 0.35 },
        );
        observer.observe(demoImage);
    }
    reducedMotion.addEventListener("change", syncDemoPlayback);
}
initDemo();

// One frame per scroll update keeps the long version history readable without a timer.
let releases = /** @type {NodeListOf<HTMLElement>} */ (
    document.querySelectorAll(".release-entry")
);
let versionLinks = document.querySelectorAll(".version-index nav a");
let readingFrame = 0;
function updateReadingPosition() {
    readingFrame = 0;
    const range = root.scrollHeight - innerHeight;
    root.style.setProperty(
        "--reading-progress",
        String(range > 0 ? Math.min(1, Math.max(0, scrollY / range)) : 0),
    );
    let current = releases[0]?.id;
    releases.forEach((release) => {
        if (release.getBoundingClientRect().top <= 170) current = release.id;
    });
    versionLinks.forEach((link) => {
        if (link.getAttribute("href") === `#${current}`)
            link.setAttribute("aria-current", "location");
        else link.removeAttribute("aria-current");
    });
}
function scheduleReadingPosition() {
    if (!readingFrame)
        readingFrame = requestAnimationFrame(updateReadingPosition);
}
addEventListener("scroll", scheduleReadingPosition, { passive: true });
addEventListener("resize", scheduleReadingPosition);
addEventListener("load", scheduleReadingPosition);
updateReadingPosition();
addEventListener("history-refreshed", () => {
    releases = document.querySelectorAll(".release-entry");
    versionLinks = document.querySelectorAll(".version-index nav a");
    scheduleReadingPosition();
});
if (document.querySelector("#history-status")) {
    import("./changelog.js")
        .then(({ refreshChangelog }) => refreshChangelog())
        .catch(() => {
            document.querySelector("#history-status").textContent =
                "当前显示随网站发布的记录。";
        });
}

async function loadFluent() {
    try {
        const componentsUrl =
            "https://unpkg.com/@fluentui/web-components@3.1.3/dist/web-components.min.js";
        const tokensUrl =
            "https://esm.sh/@fluentui/tokens@1.0.0-alpha.24?bundle";
        const [, tokens] = await Promise.all([
            import(componentsUrl),
            import(tokensUrl),
        ]);
        await customElements.whenDefined("fluent-button");
        fluent = globalThis["Fluent"];
        fluentThemes = {
            light: tokens.webLightTheme,
            dark: tokens.webDarkTheme,
        };
        if (!fluent?.setTheme || !fluentThemes.light || !fluentThemes.dark)
            throw new Error("Fluent theme API unavailable");
        applyTheme();
        const group = document.querySelector("#theme-options");
        const choices = /** @type {NodeListOf<HTMLElement>} */ (
            group.querySelectorAll("[data-theme-choice]")
        );
        function updateChoices() {
            choices.forEach((choice) => {
                const selected = choice.dataset.themeChoice === preference;
                choice.setAttribute("aria-pressed", String(selected));
                choice.setAttribute(
                    "appearance",
                    selected ? "primary" : "subtle",
                );
            });
        }
        updateChoices();
        choices.forEach((choice) => {
            choice.addEventListener("click", () => {
                const selected = choice.dataset.themeChoice;
                if (!Object.hasOwn(themeNames, selected)) return;
                preference = selected;
                try {
                    localStorage.setItem("asciistudio-theme", preference);
                } catch {
                    /* The current theme remains usable without persistence. */
                }
                applyTheme(true);
                updateChoices();
            });
        });
        document.querySelector("#theme-picker").removeAttribute("hidden");
        root.dataset.fluent = "ready";
    } catch {
        root.dataset.fluent = "unavailable";
    }
}
void loadFluent();
if (document.querySelector("#help-search")) {
    import("./help.js")
        .then(({ initHelpSearch }) => initHelpSearch())
        .catch(() => {
            /* The static questions remain available if enhancement cannot load. */
        });
}

// Copy is optional; the original text stays selectable when permission is unavailable.
document.querySelectorAll("[data-copy-target]").forEach((control) => {
    const button = /** @type {HTMLButtonElement} */ (control);
    button.hidden = false;
    button.addEventListener("click", async () => {
        const target = document.getElementById(button.dataset.copyTarget);
        const status = button
            .closest(".copy-panel")
            ?.querySelector("[role=status]");
        if (!target || !status) return;
        try {
            await navigator.clipboard.writeText(target.textContent.trim());
            status.textContent = "已复制。";
        } catch {
            status.textContent = "无法自动复制，请选中上方文字手动复制。";
        }
    });
});

// Native range controls provide keyboard and touch input without a custom drag handler.
document.querySelectorAll("[data-comparison]").forEach((element) => {
    const comparison = /** @type {HTMLElement} */ (element);
    const range = /** @type {HTMLInputElement} */ (
        comparison.querySelector('input[type="range"]')
    );
    const stage = document.getElementById(range.dataset.compareStage);
    const output = comparison.querySelector("output");
    if (!stage || !output) return;
    function updateComparison() {
        stage.style.setProperty("--split", `${range.value}%`);
        output.textContent = `${range.value}%`;
        range.setAttribute("aria-valuetext", `原图显示 ${range.value}%`);
    }
    range.addEventListener("input", updateComparison);
    updateComparison();
    comparison.hidden = false;
    const fallback = /** @type {HTMLElement} */ (
        comparison.previousElementSibling
    );
    if (fallback?.classList.contains("comparison-fallback"))
        fallback.hidden = true;
});
const filters = /** @type {HTMLElement} */ (
    document.querySelector("#gallery-filters")
);
if (filters) {
    const works = /** @type {NodeListOf<HTMLElement>} */ (
        document.querySelectorAll("[data-work-type]")
    );
    const choices = filters.querySelectorAll("[data-gallery-filter]");
    filters.hidden = false;
    choices.forEach((element) => {
        const choice = /** @type {HTMLButtonElement} */ (element);
        choice.addEventListener("click", () => {
            let visible = 0;
            works.forEach((work) => {
                work.hidden =
                    choice.dataset.galleryFilter !== "all" &&
                    work.dataset.workType !== choice.dataset.galleryFilter;
                if (!work.hidden) visible++;
            });
            choices.forEach((button) =>
                button.setAttribute("aria-pressed", String(button === choice)),
            );
            document.querySelector("#gallery-count").textContent =
                `显示 ${visible} 件作品`;
        });
    });
}
const imageDialog = /** @type {HTMLDialogElement} */ (
    document.querySelector("#image-dialog")
);
if (imageDialog && typeof imageDialog.showModal === "function") {
    document.querySelectorAll("[data-enlarge-target]").forEach((element) => {
        const button = /** @type {HTMLButtonElement} */ (element);
        const source = /** @type {HTMLImageElement} */ (
            document.getElementById(button.dataset.enlargeTarget)
        );
        if (!source) return;
        button.hidden = false;
        const fallback = /** @type {HTMLElement} */ (
            document.querySelector(`[data-image-fallback="${source.id}"]`)
        );
        if (fallback) fallback.hidden = true;
        button.addEventListener("click", () => {
            const image = /** @type {HTMLImageElement} */ (
                imageDialog.querySelector("img")
            );
            image.src = source.src;
            image.alt = source.alt;
            image.width = source.width;
            image.height = source.height;
            document.querySelector("#image-dialog-title").textContent =
                source.alt;
            imageDialog.showModal();
        });
    });
}
