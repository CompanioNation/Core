// Browser-native image processing for WASM: crop to aspect ratio and downscale, then export JPEG.
// Exposed as:
//   window.companioNationImage.processPhoto(arrayBuffer, aspectRatio, maxPixels, jpegQuality)
//   window.companioNationImage.processPhotoBase64(base64, aspectRatio, maxPixels, jpegQuality)

(function () {
    async function arrayBufferToImage(arrayBuffer) {
        const blob = new Blob([arrayBuffer]);
        const url = URL.createObjectURL(blob);
        try {
            const img = new Image();
            img.decoding = 'async';
            img.loading = 'eager';

            await new Promise((resolve, reject) => {
                img.onload = () => resolve();
                img.onerror = (e) => reject(e);
                img.src = url;
            });

            return img;
        } finally {
            URL.revokeObjectURL(url);
        }
    }

    function clamp01(v) {
        if (typeof v !== 'number' || Number.isNaN(v)) return 0.9;
        return Math.min(1, Math.max(0, v));
    }

    function base64ToUint8Array(base64) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    }

    function uint8ArrayToBase64(bytes) {
        // Chunk to avoid call stack / argument limits
        const chunkSize = 0x8000;
        let binary = '';
        for (let i = 0; i < bytes.length; i += chunkSize) {
            const chunk = bytes.subarray(i, i + chunkSize);
            binary += String.fromCharCode.apply(null, chunk);
        }
        return btoa(binary);
    }

    async function processPhoto(arrayBuffer, aspectRatio = 2, maxPixels = 1000000, jpegQuality = 0.9) {
        if (!arrayBuffer) throw new Error('arrayBuffer is required');

        const img = await arrayBufferToImage(arrayBuffer);
        const srcW = img.naturalWidth || img.width;
        const srcH = img.naturalHeight || img.height;

        if (!srcW || !srcH) throw new Error('Invalid image dimensions');

        const targetAspect = aspectRatio;
        const currentAspect = srcW / srcH;

        // Crop rectangle within source
        let cropX = 0;
        let cropY = 0;
        let cropW = srcW;
        let cropH = srcH;

        if (currentAspect > targetAspect) {
            // too wide -> crop width
            cropW = Math.round(srcH * targetAspect);
            cropX = Math.round((srcW - cropW) / 2);
        } else if (currentAspect < (1 / targetAspect)) {
            // too tall -> crop height
            cropH = Math.round(srcW * targetAspect);
            cropY = Math.round((srcH - cropH) / 2);
        }

        // Downscale to maxPixels (based on resulting crop size)
        let outW = cropW;
        let outH = cropH;
        const pixels = outW * outH;
        if (pixels > maxPixels) {
            const scale = Math.sqrt(maxPixels / pixels);
            outW = Math.max(1, Math.round(outW * scale));
            outH = Math.max(1, Math.round(outH * scale));
        }

        const canvas = document.createElement('canvas');
        canvas.width = outW;
        canvas.height = outH;

        const ctx = canvas.getContext('2d', { alpha: false });
        if (!ctx) throw new Error('Canvas 2D context not available');

        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';

        ctx.drawImage(img, cropX, cropY, cropW, cropH, 0, 0, outW, outH);

        const quality = clamp01(jpegQuality);

        const blob = await new Promise((resolve, reject) => {
            canvas.toBlob(
                (b) => (b ? resolve(b) : reject(new Error('Failed to encode JPEG'))),
                'image/jpeg',
                quality
            );
        });

        return await blob.arrayBuffer();
    }

    async function processPhotoBase64(base64, aspectRatio = 2, maxPixels = 1000000, jpegQuality = 0.9) {
        if (!base64) throw new Error('base64 is required');
        const inputBytes = base64ToUint8Array(base64);
        const outputBuffer = await processPhoto(inputBytes.buffer, aspectRatio, maxPixels, jpegQuality);
        const outputBytes = new Uint8Array(outputBuffer);
        return uint8ArrayToBase64(outputBytes);
    }

    window.companioNationImage = window.companioNationImage || {};
    window.companioNationImage.processPhoto = processPhoto;
    window.companioNationImage.processPhotoBase64 = processPhotoBase64;
})();
