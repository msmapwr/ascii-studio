// Parse the repository's Keep a Changelog subset. Content is rendered as text, never HTML.
/** @param {string} source */
export function parseChangelog(source) {
    if (source.length > 2_000_000) throw new Error("History too large");
    const headings = [
        ...source
            .replace(/\r/g, "")
            .matchAll(/^## \[([^\]]+)\] - (\d{4}-\d{2}-\d{2})$/gm),
    ];
    if (!headings.length) throw new Error("No dated versions");
    const normalized = source.replace(/\r/g, "");
    return headings.map((heading, index) => {
        const [, version, date] = heading;
        if (!/^\d+\.\d+\.\d+(?:-[\w.-]+)?$/.test(version))
            throw new Error("Invalid version");
        const section = normalized.slice(
            heading.index + heading[0].length,
            headings[index + 1]?.index,
        );
        const categories = [
            ...section.matchAll(
                /^### ([^\n]+)\n([\s\S]*?)(?=^### |(?![\s\S]))/gm,
            ),
        ]
            .map((match) => ({
                name: match[1].trim(),
                items: [...match[2].matchAll(/^- (.+)$/gm)].map(
                    (item) => item[1],
                ),
            }))
            .filter((category) => category.items.length);
        if (!categories.length) throw new Error("Empty version");
        return { version, date, categories };
    });
}

const labels = {
    Added: "新增",
    Changed: "调整",
    Deprecated: "弃用",
    Removed: "移除",
    Fixed: "修复",
    Security: "安全",
    Tested: "验证",
};
/** @param {string} tag @param {string} text @param {string} [className] */
function element(tag, text, className = "") {
    const node = document.createElement(tag);
    node.textContent = text;
    if (className) node.className = className;
    return node;
}
/** @param {string} text */
function itemContent(text) {
    const li = document.createElement("li");
    text.split(/(`[^`]+`)/).forEach((part) => {
        li.append(
            part.startsWith("`") && part.endsWith("`")
                ? element("code", part.slice(1, -1))
                : document.createTextNode(part),
        );
    });
    return li;
}
export async function refreshChangelog() {
    const status = document.querySelector("#history-status");
    const timeline = document.querySelector(".release-timeline");
    const navigation = document.querySelector(".version-index nav");
    if (!status || !timeline || !navigation) return;
    status.textContent = "正在读取仓库更新记录…";
    try {
        const response = await fetch(
            "https://raw.githubusercontent.com/msmapwr/charloom/main/CHANGELOG.md",
            {
                signal: AbortSignal.timeout(8000),
                cache: "no-cache",
                credentials: "omit",
            },
        );
        if (!response.ok) throw new Error("History unavailable");
        const versions = parseChangelog(await response.text());
        const entries = document.createDocumentFragment();
        const links = document.createDocumentFragment();
        versions.forEach(({ version, date, categories }) => {
            const anchor = `version-${version.replaceAll(".", "-")}`;
            const article = element("article", "", "release-entry");
            article.id = anchor;
            article.setAttribute("aria-labelledby", `${anchor}-title`);
            const header = element("header", "", "release-heading");
            const titleGroup = element("div", "");
            const title = element("h2", version);
            title.id = `${anchor}-title`;
            titleGroup.append(
                title,
                element(
                    "span",
                    version.includes("-") ? "预发布记录" : "版本记录",
                    "release-badge",
                ),
            );
            const time = element("time", date);
            time.setAttribute("datetime", date);
            header.append(titleGroup, time);
            article.append(header);
            categories.forEach(({ name, items }) => {
                const group = element("section", "", "change-group");
                const list = element("ul", "");
                items.forEach((item) => list.append(itemContent(item)));
                group.append(element("h3", labels[name] || name), list);
                article.append(group);
            });
            entries.append(article);
            const link = element("a", version);
            link.setAttribute("href", `#${anchor}`);
            links.append(link);
        });
        // Do not replace content while someone is reading, focused inside it, or following a version anchor.
        if (
            location.hash.startsWith("#version-") ||
            scrollY > 100 ||
            timeline.contains(document.activeElement) ||
            navigation.contains(document.activeElement)
        ) {
            status.textContent =
                "仓库记录已读取；为保留当前阅读位置，本次显示随网站发布的记录。刷新页面顶部可查看最新内容。";
            return;
        }
        timeline.replaceChildren(entries);
        navigation.replaceChildren(links);
        status.textContent =
            "已读取 main 分支的更新记录。可下载的版本请以 Releases 为准。";
        dispatchEvent(new Event("history-refreshed"));
    } catch {
        status.textContent =
            "暂时无法读取仓库，当前显示随网站发布的记录。也可打开仓库 Changelog 查看。";
    }
}
