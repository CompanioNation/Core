var _direction = "environment";

// JS interop for pinch zoom and other features.
let zoomScale = 1;
let translateX = 0;
let translateY = 0;
const maxScale = 3;
const minScale = 1;
let lastScale = 1;  // for pinch zooming
let isDragging = false;
let initialDistance = null;
let startX = 0, startY = 0, initialX = 0, initialY = 0;



window.addResizeListener = function () {
    window.resizeHandler = doResize;
    window.addEventListener('resize', doResize);
    screen.orientation.addEventListener('change', doResize);
};

window.removeResizeListener = function () {
    if (window.resizeHandler) {
        window.removeEventListener('resize', window.resizeHandler);
        screen.orientation.removeEventListener('change', doResize);
        window.resizeHandler = null;
    }
};


function getCanvasImageData(dotNetObjectReference, canvasElement) {
    canvasElement.toBlob(function (blob) {
        var reader = new FileReader();
        reader.readAsArrayBuffer(blob);
        reader.onloadend = function () {
            var byteArray = new Uint8Array(reader.result);
            dotNetObjectReference.invokeMethodAsync('ReceiveImageData', byteArray);
        };
    }, 'image/jpeg');
}

function showCameraUI() {
    document.getElementById('cameraUI').classList.remove('hidden-element');
}

function hideCameraUI() {
    document.getElementById('cameraUI').classList.add('hidden-element');
}

function initializeCamera(dotNetObject, videoElement, canvasElement) {
    zoomScale = 1;
    translateX = 0;
    translateY = 0;

    canvasElement.style.display = 'none';
    videoElement.style.display = 'block';

    navigator.mediaDevices.getUserMedia({ video: { facingMode: _direction } })
        .then(stream => {
            videoElement.srcObject = stream;
            videoElement.onloadedmetadata = () => {
                // Trigger resize to adjust for orientation and screen size
                doResize();
                // This event is fired when enough metadata is loaded to start playing
                dotNetObject.invokeMethodAsync('OnCameraReady');
            };
        })
        .catch(error => {
            if (error.name === 'NotReadableError') {
                // If the device is in use, retry after a delay
                alert("Your camera is already in use. Please close whatever app is using the camera, and try again.");
            } else if (error.name == 'NotFoundError') {
                alert("You do not seem to have an accessible camera.")
            } else if (error.name == 'NotAllowedError') {
                alert("You must allow access to the camera for this feature to work.")
            } else {
                alert("An unknown error occurred: " + error);
                console.error('Error accessing camera:', error);
            }
            cameraInterop.closeCamera(videoElement, canvasElement);
        });
}

function debounce(callback, delay) {
    let timer
    return function () {
        clearTimeout(timer)
        timer = setTimeout(() => {
            callback();
        }, delay)
    }
}

function doResize() {
    if (document.getElementById('cameraUI') == null) {
        window.removeResizeListener();
        return;
    }
    debounce(doResizeAfterDelay, 100)();
}
function doResizeAfterDelay() {
    const containerRect = cameraUI.getBoundingClientRect();
    const cameraWidth = videoElement.videoWidth;
    const cameraHeight = videoElement.videoHeight;
    const videoAspectRatio = cameraWidth / cameraHeight;
    const containerAspectRatio = containerRect.width / containerRect.height;

    let newWidth, newHeight;

    if (containerAspectRatio > videoAspectRatio) {
        // Container is wider than video, fit to height
        newHeight = containerRect.height;
        newWidth = newHeight * videoAspectRatio;
    } else {
        // Container is taller than video, fit to width
        newWidth = containerRect.width;
        newHeight = newWidth / videoAspectRatio;
    }

    // Calculate the previous container size
    const previousContainerWidth = cameraContainer.clientWidth || newWidth;
    const previousContainerHeight = cameraContainer.clientHeight || newHeight;

    cameraContainer.style.width = `${newWidth}px`;
    cameraContainer.style.height = `${newHeight}px`;
    const newTop = (containerRect.height - newHeight) / 3;
    const newLeft = (containerRect.width - newWidth) / 2;
    cameraContainer.style.top = `${newTop}px`;
    cameraContainer.style.left = `${newLeft}px`;

    // Calculate the ratio of the new container size to the previous size
    const widthRatio = newWidth / previousContainerWidth;
    const heightRatio = newHeight / previousContainerHeight;

    // Adjust the translateX and translateY based on the size change
    translateX *= widthRatio;
    translateY *= heightRatio;

    // Ensure the translation stays within the valid bounds
    clampPan();
    updateTransform();
}


function switchCamera(dotNetObject, videoElement, canvasElement) {
    stopCameraVideo(videoElement);

    // Implement switch camera logic.
    if (_direction == "environment") _direction = "user";
    else _direction = "environment";
    initializeCamera(dotNetObject, videoElement, canvasElement);
}

function takePhoto(videoElement, canvasElement) {
    requestAnimationFrame(() => {
        const context = canvasElement.getContext('2d');

        // Make sure the camera mirroring for the "user" facing camera is properly handled
        var translateXfinal, translateYfinal;
        if (_direction == "user") {
            translateXfinal = -translateX;
            translateYfinal = translateY;
        }
        else {
            translateXfinal = translateX;
            translateYfinal = translateY;
        }

        // Find middle of total, subtract 1/2 width of window, subtract translate
        const containerRect = cameraContainer.getBoundingClientRect();

        const totalWidth = containerRect.width * zoomScale;
        const totalHeight = containerRect.height * zoomScale;
        // window top, left is going to be the CENTER - 1/2 viewport - translateX,Y
        const viewLeft = (totalWidth / 2) - (containerRect.width / 2) - translateXfinal;
        const viewTop = (totalHeight / 2) - (containerRect.height / 2) - translateYfinal;
        const Xpercent = viewLeft / totalWidth;
        const Ypercent = viewTop / totalHeight;
        // Now choose the top,left of the full video size
        const scaledX = videoElement.videoWidth * Xpercent;
        const scaledY = videoElement.videoHeight * Ypercent;

        // The width and height will be the inverse scale percentage
        // So if the scale is doubled, then the size will be half of the full
        const sWidth = videoElement.videoWidth / zoomScale;
        const sHeight = videoElement.videoHeight / zoomScale;

        // Specify the number of pixels stored on the canvas
        canvasElement.width = sWidth;
        canvasElement.height = sHeight;

        context.drawImage(videoElement, scaledX, scaledY, sWidth, sHeight, 0, 0, canvasElement.width, canvasElement.height);

        canvasElement.style.display = 'block';
        videoElement.style.display = 'none';
    });
}


function retakePhoto(videoElement, canvasElement) {
    canvasElement.style.display = 'none';
    videoElement.style.display = 'block';
}

function euclideanDistance(x1, y1, x2, y2) {
    return Math.sqrt(Math.pow(x1 - x2, 2) + Math.pow(y1 - y2, 2));
}

function enablePinchZoomAndPan(videoElement) {
    videoElement.addEventListener('touchstart', handleTouchStart);
    videoElement.addEventListener('touchmove', handleTouchMove);
    videoElement.addEventListener('touchend', handleTouchEnd);
}

function enableMouseZoomAndPan(videoElement) {
    videoElement.addEventListener('wheel', handleMouseWheel);
    videoElement.addEventListener('mousedown', handleMouseDown);
    videoElement.addEventListener('mousemove', handleMouseMove);
    videoElement.addEventListener('mouseup', handleMouseUp);
    videoElement.addEventListener('mouseleave', handleMouseUp);
}
function handleTouchStart(event) {
    if (event.touches.length === 2) {
        isDragging = false; // Cancel the dragging operation, so that it doesn't jump suddenly
        event.preventDefault();
        const touch1 = event.touches[0];
        const touch2 = event.touches[1];
        initialDistance = getDistance(touch1, touch2);
        startX = (touch1.pageX + touch2.pageX) / 2;
        startY = (touch1.pageY + touch2.pageY) / 2;
        initialX = translateX;
        initialY = translateY;
        lastScale = zoomScale;  //
    } else if (event.touches.length === 1 && zoomScale > 1) {
        isDragging = true;
        startX = event.touches[0].pageX;
        startY = event.touches[0].pageY;
        initialX = translateX;
        initialY = translateY;
    }
}

function handleTouchMove(event) {
    if (event.touches.length === 2 && initialDistance) {
        event.preventDefault();
        const touch1 = event.touches[0];
        const touch2 = event.touches[1];
        const newDistance = getDistance(touch1, touch2);

        // Calculate the new scale based on the distance between the two touch points
        const newScale = Math.max(minScale, Math.min(lastScale * (newDistance / initialDistance), maxScale));

        const containerRect = cameraContainer.getBoundingClientRect();
        const positionX = (touch1.clientX + touch2.clientX) / 2;
        const positionY = (touch1.clientY + touch2.clientY) / 2;

        // Calculate the deltaX and deltaY considering the current translation
        const deltaX = (positionX - containerRect.left - containerRect.width / 2 - translateX) * (1 - newScale / zoomScale);
        const deltaY = (positionY - containerRect.top - containerRect.height / 2 - translateY) * (1 - newScale / zoomScale);

        translateX += deltaX;
        translateY += deltaY;

        zoomScale = newScale;  // Update the scale to the new value

        clampPan();
        updateTransform();

    } else if (event.touches.length === 1 && isDragging) {
        const moveX = event.touches[0].pageX - startX;
        const moveY = event.touches[0].pageY - startY;

        translateX = initialX + moveX;
        translateY = initialY + moveY;
        clampPan();
        updateTransform();
    }
}

function handleTouchEnd(event) {
    if (event.touches.length === 0) {
        isDragging = false;
        initialDistance = null;
    }
}

function handleMouseWheel(event) {
    event.preventDefault();

    const containerRect = cameraContainer.getBoundingClientRect();

    const scaleAmount = 0.1;
    const oldScale = zoomScale;

    zoomScale = event.deltaY < 0 ? Math.min(zoomScale + scaleAmount, maxScale) : Math.max(zoomScale - scaleAmount, minScale);

    // Adjust deltaX and deltaY considering the current translation
    const deltaX = (event.clientX - containerRect.left - containerRect.width / 2 - translateX) * (1 - zoomScale / oldScale);
    const deltaY = (event.clientY - containerRect.top - containerRect.height / 2 - translateY) * (1 - zoomScale / oldScale);

    // Update translation
    translateX += deltaX;
    translateY += deltaY;

    clampPan();
    updateTransform();
}

function handleMouseDown(event) {
    if (zoomScale > 1) {
        isDragging = true;
        startX = event.pageX;
        startY = event.pageY;
        initialX = translateX;
        initialY = translateY;
    }
}

function handleMouseMove(event) {
    if (isDragging) {
        const moveX = event.pageX - startX;
        const moveY = event.pageY - startY;

        translateX = initialX + moveX;
        translateY = initialY + moveY;

        clampPan();
        updateTransform();
    }
}

function handleMouseUp() {
    isDragging = false;
}

function clampPan() {
    const containerRect = cameraContainer.getBoundingClientRect();
    const halfWidth = containerRect.width / 2;
    const halfHeight = containerRect.height / 2;

    // Clamp the panning to ensure the video doesn't move out of the container bounds
    const minTranslateX = Math.min(containerRect.width * (1 - zoomScale) / 2, 0);
    const minTranslateY = Math.min(containerRect.height * (1 - zoomScale) / 2, 0);
    const maxTranslateX = -minTranslateX;
    const maxTranslateY = -minTranslateY;

    if (translateX < minTranslateX) translateX = minTranslateX;
    if (translateY < minTranslateY) translateY = minTranslateY;
    if (translateX > maxTranslateX) translateX = maxTranslateX;
    if (translateY > maxTranslateY) translateY = maxTranslateY;
}

function updateTransform() {

    var scaleX, scaleY;

    // Make sure the camera mirroring for the "user" facing camera is properly handled
    //  IE: mirror the selfie camera.
    if (_direction == "user") {
        scaleX = -zoomScale;
        scaleY = zoomScale;
    }
    else {
        scaleX = zoomScale;
        scaleY = zoomScale;
    }

    videoElement.style.transform = `translate(${translateX}px, ${translateY}px) scale(${scaleX},${scaleY})`;
}

function getDistance(touch1, touch2) {
    return Math.hypot(touch2.pageX - touch1.pageX, touch2.pageY - touch1.pageY);
}


function drawTextOnCanvas(canvasElement, text) {
    const context = canvasElement.getContext('2d');
    context.font = "20px Arial";
    context.fillStyle = "black";
    context.textAlign = "center";
    context.textBaseline = "middle";

    // Clear the canvas before drawing text
    context.clearRect(0, 0, canvasElement.width, canvasElement.height);

    context.fillText(text, canvasElement.width / 2, canvasElement.height / 2);
}

function stopCameraVideo(videoElement) {
    if (videoElement && videoElement.srcObject) {
        videoElement.srcObject.getTracks().forEach(function (track) {
            track.stop();
        });
        videoElement.srcObject = null;
    }
}
function closeCamera(videoElement, canvasElement) {
    stopCameraVideo(videoElement);
    hideCameraUI(videoElement, canvasElement);
}

function setImageSource(imageElementId, imageData) {
    document.getElementById(imageElementId).src = imageData;
}


window.cameraInterop = {
    showCameraUI: showCameraUI,
    hideCameraUI: hideCameraUI,
    initializeCamera: initializeCamera,
    takePhoto: takePhoto,
    retakePhoto: retakePhoto,
    switchCamera: switchCamera,
    enablePinchZoomAndPan: enablePinchZoomAndPan,
    enableMouseZoomAndPan: enableMouseZoomAndPan,
    drawTextOnCanvas: drawTextOnCanvas,
    closeCamera: closeCamera,
    getCanvasImageData: getCanvasImageData,
    setImageSource: setImageSource
};

