let joystickSupportStatus = null;

function scSetJoystickSupportFlag(value) {
    if (value === true || value === false) {
        joystickSupportStatus = value;
    }
}

function scUpdateBindingNotice(selectElement, noticeElement) {
    if (!selectElement || !noticeElement) return;

    const selected = selectElement.selectedOptions && selectElement.selectedOptions[0];
    const bindingType = selected ? (selected.dataset.bindingType || '').toLowerCase() : '';

    if (bindingType === 'joystick') {
        noticeElement.classList.remove('hidden');
        noticeElement.textContent = joystickSupportStatus === false
            ? 'Joystick binding selected. No virtual joystick was detected, so this binding may be skipped. Enable a virtual joystick driver to emulate this binding.'
            : 'Joystick binding selected. The plugin will emulate this joystick button when no keyboard binding is available.';
    } else {
        noticeElement.classList.add('hidden');
        noticeElement.textContent = '';
    }
}

function scWireBindingNotice(selectId, noticeId) {
    const select = document.getElementById(selectId);
    const notice = document.getElementById(noticeId);
    if (!select || !notice) return;

    select.addEventListener('change', () => scUpdateBindingNotice(select, notice));
    scUpdateBindingNotice(select, notice);
}
