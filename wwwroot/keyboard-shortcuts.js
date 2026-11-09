// keyboard-shortcuts.js - Global shortcuts for Notepad AI (Ctrl+K, Esc, Ctrl+1, Ctrl+2)
window.keyboardShortcuts = {
    dotNetRef: null,
    isRegistered: false,

    registerGlobalShortcuts: function (dotNetHelper) {
        this.dotNetRef = dotNetHelper;
        if (this.isRegistered) return;
        this.isRegistered = true;

        window.addEventListener("keydown", (e) => {
            // Check for Ctrl+K or Cmd+K
            if ((e.ctrlKey || e.metaKey) && (e.key === "k" || e.key === "K")) {
                e.preventDefault();
                if (this.dotNetRef) {
                    this.dotNetRef.invokeMethodAsync("OnGlobalShortcut", "k");
                }
                return;
            }

            // Check for Escape
            if (e.key === "Escape") {
                if (this.dotNetRef) {
                    this.dotNetRef.invokeMethodAsync("OnGlobalShortcut", "Escape");
                }
                return;
            }

            // Ctrl+1 (Dashboard / Discoveries)
            if ((e.ctrlKey || e.metaKey) && e.key === "1") {
                e.preventDefault();
                if (this.dotNetRef) {
                    this.dotNetRef.invokeMethodAsync("OnGlobalShortcut", "1");
                }
                return;
            }

            // Ctrl+2 (Library)
            if ((e.ctrlKey || e.metaKey) && e.key === "2") {
                e.preventDefault();
                if (this.dotNetRef) {
                    this.dotNetRef.invokeMethodAsync("OnGlobalShortcut", "2");
                }
                return;
            }
        });
    }
};
