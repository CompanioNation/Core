export function initializeFeedbackButton(helper) {
    let dotNetHelper = helper; // Declare the variable before using it.

    const button = document.querySelector('.feedbackButtonClass');
    const savedPosition = localStorage.getItem('feedbackButtonPosition');
    if (savedPosition) {
        const { top, left } = JSON.parse(savedPosition);
        button.style.top = top;
        button.style.left = left;
    }

    let isDragging = false;
    let startX, startY, initialX, initialY;
    const dragThreshold = 5;
    let dragStartPos = { x: 0, y: 0 };

    button.addEventListener('mousedown', (e) => {
        dragStartPos = { x: e.clientX, y: e.clientY };
        startX = e.clientX;
        startY = e.clientY;
        initialX = button.offsetLeft;
        initialY = button.offsetTop;
        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
    });

    button.addEventListener('touchstart', (e) => {
        dragStartPos = { x: e.touches[0].clientX, y: e.touches[0].clientY };
        startX = e.touches[0].clientX;
        startY = e.touches[0].clientY;
        initialX = button.offsetLeft;
        initialY = button.offsetTop;
        document.addEventListener('touchmove', onTouchMove);
        document.addEventListener('touchend', onTouchEnd);
    });

    function checkIfDragging(currentX, currentY) {
        if (!isDragging) {
            const deltaX = Math.abs(currentX - dragStartPos.x);
            const deltaY = Math.abs(currentY - dragStartPos.y);
            if (deltaX > dragThreshold || deltaY > dragThreshold) {
                isDragging = true;
                dotNetHelper.invokeMethodAsync('SetDragging', true);
            }
        }
    }

    function onMouseMove(e) {
        checkIfDragging(e.clientX, e.clientY);
        if (isDragging) {
            const dx = e.clientX - startX;
            const dy = e.clientY - startY;
            button.style.left = `${initialX + dx}px`;
            button.style.top = `${initialY + dy}px`;
        }
    }

    function onTouchMove(e) {
        checkIfDragging(e.touches[0].clientX, e.touches[0].clientY);
        if (isDragging) {
            const dx = e.touches[0].clientX - startX;
            const dy = e.touches[0].clientY - startY;
            button.style.left = `${initialX + dx}px`;
            button.style.top = `${initialY + dy}px`;
        }
    }

    function endDrag() {
        if (isDragging) {
            isDragging = false;
            dotNetHelper.invokeMethodAsync('SetDragging', false);
            localStorage.setItem('feedbackButtonPosition', JSON.stringify({
                top: button.style.top,
                left: button.style.left
            }));
        }
    }

    function onMouseUp() {
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
        endDrag();
    }

    function onTouchEnd() {
        document.removeEventListener('touchmove', onTouchMove);
        document.removeEventListener('touchend', onTouchEnd);
        endDrag();
    }
}