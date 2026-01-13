window.indexedDbHelper = {
    openDb: function (dbName, version, storeSchemas) {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(dbName, version);
            request.onupgradeneeded = (event) => {
                const db = event.target.result;
                storeSchemas.forEach(schema => {
                    if (!db.objectStoreNames.contains(schema.name)) {
                        const store = db.createObjectStore(schema.name, { keyPath: schema.primaryKey });
                        schema.indexes.forEach(index => {
                            store.createIndex(index.name, index.keyPath, { unique: index.unique });
                        });
                    }
                });
            };
            request.onsuccess = (event) => {
                const db = event.target.result;
                if (!this.validateDbFormat(db, storeSchemas)) {
                    db.close();
                    indexedDB.deleteDatabase(dbName).onsuccess = () => {
                        this.openDb(dbName, version, storeSchemas).then(resolve).catch(reject);
                    };
                } else {
                    resolve(db);
                }
            };
            request.onerror = (event) => {
                reject(event.target.error);
            };
        });
    },
    validateDbFormat: function (db, storeSchemas) {
        for (const schema of storeSchemas) {
            if (!db.objectStoreNames.contains(schema.name)) {
                return false;
            }
            const store = db.transaction(schema.name, 'readonly').objectStore(schema.name);
            if (store.keyPath !== schema.primaryKey) {
                return false;
            }
            for (const index of schema.indexes) {
                if (!store.indexNames.contains(index.name)) {
                    return false;
                }
            }
        }
        return true;
    },
    addRecord: function (dbName, storeName, record) {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(dbName);
            request.onsuccess = (event) => {
                const db = event.target.result;
                const transaction = db.transaction(storeName, 'readwrite');
                const store = transaction.objectStore(storeName);
                const addRequest = store.add(record);
                addRequest.onsuccess = () => {
                    resolve();
                };
                addRequest.onerror = (event) => {
                    reject(event.target.error);
                };
            };
            request.onerror = (event) => {
                reject(event.target.error);
            };
        });
    },
    getRecords: function (dbName, storeName) {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(dbName);
            request.onsuccess = (event) => {
                const db = event.target.result;
                const transaction = db.transaction(storeName, 'readonly');
                const store = transaction.objectStore(storeName);
                const getAllRequest = store.getAll();
                getAllRequest.onsuccess = () => {
                    resolve(getAllRequest.result);
                };
                getAllRequest.onerror = (event) => {
                    reject(event.target.error);
                };
            };
            request.onerror = (event) => {
                reject(event.target.error);
            };
        });
    }
};
