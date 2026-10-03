export function initHelpSearch() {
    const search = /** @type {HTMLElement} */ (
        document.querySelector("#help-search")
    );
    const input = /** @type {HTMLInputElement} */ (
        document.querySelector("#help-query")
    );
    const clear = document.querySelector("#help-clear");
    const count = document.querySelector("#help-count");
    const empty = /** @type {HTMLElement} */ (
        document.querySelector("#help-empty")
    );
    if (!search || !input || !clear || !count || !empty) return;
    const items = Array.from(document.querySelectorAll("[data-help-item]")).map(
        (node) => {
            const item = /** @type {HTMLDetailsElement} */ (node);
            return {
                item,
                text: item.textContent
                    .normalize("NFKC")
                    .toLocaleLowerCase("zh-CN"),
                open: item.open,
            };
        },
    );
    const groups = /** @type {NodeListOf<HTMLElement>} */ (
        document.querySelectorAll("[data-help-group]")
    );
    function update() {
        const words = input.value
            .normalize("NFKC")
            .toLocaleLowerCase("zh-CN")
            .trim()
            .split(/\s+/)
            .filter(Boolean);
        let matches = 0;
        items.forEach(({ item, text, open }) => {
            item.hidden = !words.every((word) => text.includes(word));
            item.open = words.length ? !item.hidden : open;
            if (!item.hidden) matches++;
        });
        groups.forEach((group) => {
            group.hidden = !group.querySelector(
                "[data-help-item]:not([hidden])",
            );
        });
        count.textContent = words.length
            ? `找到 ${matches} 个问题。`
            : `共 ${items.length} 个常见问题。`;
        empty.hidden = matches !== 0;
    }
    input.addEventListener("input", update);
    clear.addEventListener("click", () => {
        input.value = "";
        update();
        input.focus();
    });
    search.hidden = false;
    update();
}
