// sqlite-storage.js - Embedded SQLite in the browser (sql.js) with IndexedDB persistence
window.sqliteStorage = {
    db: null,
    dbName: "notepad_ai_sqlite_db",
    storeName: "sqlite_file",
    isInitialized: false,

    async init() {
        if (this.isInitialized && this.db) return true;

        try {
            const SQL = await window.initSqlJs({
                locateFile: file => `lib/${file}`
            });

            // Load saved database binary from IndexedDB if available
            const savedData = await this.loadFromIndexedDB();
            if (savedData && savedData.byteLength > 0) {
                this.db = new SQL.Database(new Uint8Array(savedData));
                console.log("[SQLite] Loaded existing SQLite database from browser storage.");
            } else {
                this.db = new SQL.Database();
                console.log("[SQLite] Initialized new in-memory SQLite database.");
            }

            // Execute DDL SQL schema
            this.createSchema();
            await this.saveToIndexedDB();

            this.isInitialized = true;
            return true;
        } catch (err) {
            console.error("[SQLite] Error initializing database:", err);
            return false;
        }
    },

    createSchema() {
        const schema = `
            CREATE TABLE IF NOT EXISTS documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                filename TEXT NOT NULL,
                uploaded_at TEXT NOT NULL,
                processing_status TEXT NOT NULL,
                chunk_count INTEGER NOT NULL DEFAULT 0,
                has_map INTEGER NOT NULL DEFAULT 0,
                cover_url TEXT,
                raw_text TEXT
            );

            CREATE TABLE IF NOT EXISTS source_chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                page_number INTEGER NOT NULL,
                chunk_index INTEGER NOT NULL,
                content TEXT NOT NULL,
                document_filename TEXT,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS concept_nodes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                label TEXT NOT NULL,
                explanation TEXT,
                node_type TEXT NOT NULL DEFAULT 'concept',
                x REAL NOT NULL,
                y REAL NOT NULL,
                source_chunk_ids TEXT,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS concept_edges (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                source_node_id INTEGER NOT NULL,
                target_node_id INTEGER NOT NULL,
                relationship TEXT,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                citations_json TEXT,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );
        `;
        this.db.run(schema);
    },

    // Raw SQL Execution
    async executeSql(sql, params = []) {
        if (!this.db) await this.init();
        try {
            this.db.run(sql, params);
            await this.saveToIndexedDB();
            return { success: true };
        } catch (err) {
            console.error("[SQLite executeSql error]", sql, err);
            return { success: false, error: err.message };
        }
    },

    // SQL Query returning JSON list of rows
    async querySql(sql, params = []) {
        if (!this.db) await this.init();
        try {
            const stmt = this.db.prepare(sql, params);
            const rows = [];
            while (stmt.step()) {
                rows.push(stmt.getAsObject());
            }
            stmt.free();
            return JSON.stringify(rows);
        } catch (err) {
            console.error("[SQLite querySql error]", sql, err);
            return JSON.stringify([]);
        }
    },

    // Save SQLite database snapshot to browser IndexedDB
    loadFromIndexedDB() {
        return new Promise((resolve, reject) => {
            const req = indexedDB.open(this.dbName, 1);
            req.onupgradeneeded = e => {
                const db = e.target.result;
                if (!db.objectStoreNames.contains(this.storeName)) {
                    db.createObjectStore(this.storeName);
                }
            };
            req.onsuccess = e => {
                const db = e.target.result;
                const tx = db.transaction(this.storeName, "readonly");
                const store = tx.objectStore(this.storeName);
                const getReq = store.get("db_binary");
                getReq.onsuccess = () => resolve(getReq.result);
                getReq.onerror = () => reject(getReq.error);
            };
            req.onerror = () => reject(req.error);
        });
    },

    saveToIndexedDB() {
        if (!this.db) return Promise.resolve();
        const binary = this.db.export();
        return new Promise((resolve, reject) => {
            const req = indexedDB.open(this.dbName, 1);
            req.onupgradeneeded = e => {
                const db = e.target.result;
                if (!db.objectStoreNames.contains(this.storeName)) {
                    db.createObjectStore(this.storeName);
                }
            };
            req.onsuccess = e => {
                const db = e.target.result;
                const tx = db.transaction(this.storeName, "readwrite");
                const store = tx.objectStore(this.storeName);
                store.put(binary, "db_binary");
                tx.oncomplete = () => resolve(true);
                tx.onerror = () => reject(tx.error);
            };
            req.onerror = () => reject(req.error);
        });
    }
};

// Document extraction helper supporting PDF, Markdown, and TXT
window.docParser = {
    async extractPdfText(arrayBuffer) {
        if (!window.pdfjsLib) {
            throw new Error("PDF.js library is not loaded.");
        }
        window.pdfjsLib.GlobalWorkerOptions.workerSrc = 'lib/pdf.worker.min.js';

        const loadingTask = window.pdfjsLib.getDocument({ data: arrayBuffer });
        const pdf = await loadingTask.promise;
        const pages = [];

        for (let i = 1; i <= pdf.numPages; i++) {
            const page = await pdf.getPage(i);
            const textContent = await page.getTextContent();
            const pageText = textContent.items.map(item => item.str).join(" ");
            pages.push({ pageNumber: i, content: pageText });
        }

        return JSON.stringify(pages);
    }
};

