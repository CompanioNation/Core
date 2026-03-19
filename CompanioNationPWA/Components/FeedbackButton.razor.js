export function initializeFeedbackButton(dotNetHelper) {
    const button = document.querySelector('.feedbackButtonClass');
    if (!button) return;

    const savedPosition = localStorage.getItem('feedbackButtonPosition');
    if (savedPosition) {
        const { top, left } = JSON.parse(savedPosition);
        button.style.top = top;
        button.style.left = left;
        button.style.right = 'auto';
    }

    let isDragging = false;
    let startX, startY, initialX, initialY;
    const dragThreshold = 8;

    function startInteraction(clientX, clientY) {
        startX = clientX;
        startY = clientY;
        initialX = button.offsetLeft;
        initialY = button.offsetTop;
        isDragging = false;
        button.classList.add('dragging');
    }

    function clamp(value, min, max) {
        return Math.min(Math.max(value, min), max);
    }

    function moveButton(clientX, clientY) {
        const deltaX = Math.abs(clientX - startX);
        const deltaY = Math.abs(clientY - startY);

        if (!isDragging && (deltaX > dragThreshold || deltaY > dragThreshold)) {
            isDragging = true;
        }

        if (isDragging) {
            const maxX = window.innerWidth - button.offsetWidth;
            const maxY = window.innerHeight - button.offsetHeight;
            button.style.left = `${clamp(initialX + clientX - startX, 0, maxX)}px`;
            button.style.top = `${clamp(initialY + clientY - startY, 0, maxY)}px`;
            button.style.right = 'auto';
        }
    }

    function endInteraction() {
        button.classList.remove('dragging');
        if (isDragging) {
            isDragging = false;
            localStorage.setItem('feedbackButtonPosition', JSON.stringify({
                top: button.style.top,
                left: button.style.left
            }));
        } else {
            dotNetHelper.invokeMethodAsync('ShowPopup');
        }
    }

    // --- Mouse ---------------------------------------------------------------
    function onMouseMove(e) {
        e.preventDefault();
        moveButton(e.clientX, e.clientY);
    }

    function onMouseUp() {
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
        endInteraction();
    }

    button.addEventListener('mousedown', (e) => {
        e.preventDefault();
        startInteraction(e.clientX, e.clientY);
        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
    });

    // --- Touch ---------------------------------------------------------------
    function onTouchMove(e) {
        moveButton(e.touches[0].clientX, e.touches[0].clientY);
    }

    function onTouchEnd() {
        document.removeEventListener('touchmove', onTouchMove);
        document.removeEventListener('touchend', onTouchEnd);
        endInteraction();
    }

    button.addEventListener('touchstart', (e) => {
        startInteraction(e.touches[0].clientX, e.touches[0].clientY);
        document.addEventListener('touchmove', onTouchMove);
        document.addEventListener('touchend', onTouchEnd);
    });
}