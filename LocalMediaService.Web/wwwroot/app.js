const state = {
    csrfToken: '',
    services: [],
    media: []
};

const elements = {
    services: document.getElementById('services'),
    mediaGroups: document.getElementById('media-groups'),
    libraryStatus: document.getElementById('library-status'),
    mediaSearch: document.getElementById('media-search'),
    accountDialog: document.getElementById('account-dialog'),
    accountsDialog: document.getElementById('accounts-dialog'),
    revealDialog: document.getElementById('reveal-dialog'),
    playerDialog: document.getElementById('player-dialog'),
    player: document.getElementById('media-player'),
    toast: document.getElementById('toast')
};

async function api(path, options = {}) {
    const request = { cache: 'no-store', ...options };
    request.headers = new Headers(options.headers || {});
    if (request.body && !request.headers.has('Content-Type')) {
        request.headers.set('Content-Type', 'application/json');
    }
    if (request.method && !['GET', 'HEAD'].includes(request.method.toUpperCase())) {
        request.headers.set('X-CSRF-TOKEN', state.csrfToken);
    }

    const response = await fetch(path, request);
    if (response.status === 401) {
        window.location.replace('/login');
        throw new Error('Your session has expired.');
    }
    return response;
}

async function readProblem(response, fallback) {
    try {
        const problem = await response.json();
        if (problem.title) return problem.title;
        if (problem.errors) return Object.values(problem.errors).flat().join(' ');
    } catch {
        // The fallback below is deliberately used for non-JSON failures.
    }
    return fallback;
}

function make(tag, className, text) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
}

function button(text, className, handler) {
    const element = make('button', className, text);
    element.type = 'button';
    element.addEventListener('click', handler);
    return element;
}

function showToast(message) {
    elements.toast.textContent = message;
    elements.toast.hidden = false;
    window.clearTimeout(showToast.timer);
    showToast.timer = window.setTimeout(() => { elements.toast.hidden = true; }, 3500);
}

function openAccountDialog(serviceId = '') {
    const select = document.getElementById('account-service');
    select.replaceChildren(...state.services.map(service => {
        const option = document.createElement('option');
        option.value = service.id;
        option.textContent = service.name;
        return option;
    }));
    if (serviceId) select.value = serviceId;
    document.getElementById('account-form').reset();
    if (serviceId) select.value = serviceId;
    document.getElementById('account-error').hidden = true;
    elements.accountDialog.showModal();
}

function renderServices() {
    elements.services.replaceChildren();
    for (const service of state.services) {
        const card = make('article', 'service-card');
        card.dataset.service = service.id;

        const top = make('div', 'service-card-top');
        const logo = make('div', 'service-logo', service.name.slice(0, 2).toUpperCase());
        const accountCount = make(
            'span',
            service.accounts.length ? 'account-count has-accounts' : 'account-count',
            service.accounts.length ? `${service.accounts.length} saved` : 'No saved account');
        top.append(logo, accountCount);

        const name = make('h3', '', service.name);
        const description = make('p', '', service.description);
        const actions = make('div', 'service-actions');
        const open = make('a', 'button button-primary', 'Open');
        open.href = service.homeUrl;
        open.target = '_blank';
        open.rel = 'noopener noreferrer';
        const signIn = make('a', 'button button-ghost', 'Provider sign-in');
        signIn.href = service.loginUrl;
        signIn.target = '_blank';
        signIn.rel = 'noopener noreferrer';
        actions.append(open, signIn);

        const vaultActions = make('div', 'vault-actions');
        vaultActions.append(button('Save account', 'text-button', () => openAccountDialog(service.id)));
        if (service.accounts.length) {
            vaultActions.append(button('Manage', 'text-button', () => showAccounts(service)));
        }

        card.append(top, name, description, actions, vaultActions);
        elements.services.append(card);
    }
}

function showAccounts(service) {
    document.getElementById('accounts-title').textContent = `${service.name} accounts`;
    const list = document.getElementById('accounts-list');
    list.replaceChildren();
    for (const account of service.accounts) {
        const row = make('div', 'account-row');
        const details = make('div');
        details.append(
            make('strong', '', account.label),
            make('span', 'account-hint', account.usernameHint));
        const actions = make('div', 'account-row-actions');
        actions.append(
            button('Reveal', 'button button-secondary button-small', () => openReveal(account.id)),
            button('Delete', 'button button-danger button-small', () => deleteAccount(account.id, account.label)));
        row.append(details, actions);
        list.append(row);
    }
    elements.accountsDialog.showModal();
}

function openReveal(accountId) {
    document.getElementById('reveal-account-id').value = accountId;
    document.getElementById('reveal-admin-password').value = '';
    document.getElementById('revealed-username').value = '';
    document.getElementById('revealed-password').value = '';
    document.getElementById('revealed-fields').hidden = true;
    document.getElementById('reveal-submit').hidden = false;
    document.getElementById('reveal-error').hidden = true;
    elements.accountsDialog.close();
    elements.revealDialog.showModal();
}

async function deleteAccount(id, label) {
    if (!window.confirm(`Delete the saved account “${label}”?`)) return;
    const response = await api(`/api/accounts/${id}`, { method: 'DELETE' });
    if (!response.ok) {
        showToast(await readProblem(response, 'The account could not be deleted.'));
        return;
    }
    elements.accountsDialog.close();
    await loadServices();
    showToast('Saved account deleted.');
}

function formatSize(bytes) {
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
        value /= 1024;
        unit += 1;
    }
    return `${value.toFixed(unit > 1 ? 1 : 0)} ${units[unit]}`;
}

function renderMedia() {
    const query = elements.mediaSearch.value.trim().toLocaleLowerCase();
    const visible = state.media.filter(item =>
        !query || `${item.title} ${item.relativePath}`.toLocaleLowerCase().includes(query));
    elements.mediaGroups.replaceChildren();

    if (!visible.length) {
        elements.mediaGroups.append(make(
            'div',
            'empty-state',
            state.media.length ? 'No titles match your search.' : 'No supported video files were found.'));
        return;
    }

    const groups = new Map();
    for (const item of visible) {
        if (!groups.has(item.category)) groups.set(item.category, []);
        groups.get(item.category).push(item);
    }
    for (const [category, items] of groups) {
        const section = make('section', 'media-group');
        section.append(make('h3', 'media-group-title', category));
        const grid = make('div', 'media-grid');
        for (const item of items) {
            const card = button('', 'media-card', () => playMedia(item));
            card.setAttribute('aria-label', `Play ${item.title}`);
            const art = make('div', 'media-art');
            art.append(make('span', 'play-symbol', '▶'));
            if (item.subtitles.length) art.append(make('span', 'subtitle-badge', 'CC'));
            const details = make('div', 'media-card-copy');
            details.append(
                make('strong', '', item.title),
                make('span', '', `${formatSize(item.sizeBytes)} · ${item.contentType.replace('video/', '').toUpperCase()}`));
            if (!item.directPlayLikely) details.append(make('span', 'format-warning', 'Compatibility varies'));
            card.append(art, details);
            grid.append(card);
        }
        section.append(grid);
        elements.mediaGroups.append(section);
    }
}

function playMedia(item) {
    elements.player.pause();
    elements.player.removeAttribute('src');
    for (const track of [...elements.player.querySelectorAll('track')]) track.remove();
    for (const subtitle of item.subtitles) {
        const track = document.createElement('track');
        track.kind = 'subtitles';
        track.label = subtitle.label;
        track.srclang = subtitle.language;
        track.src = subtitle.url;
        elements.player.append(track);
    }
    elements.player.src = item.streamUrl;
    document.getElementById('player-title').textContent = item.title;
    document.getElementById('player-category').textContent = item.category;
    document.getElementById('player-path').textContent = item.relativePath;
    document.getElementById('player-warning').hidden = item.directPlayLikely;
    elements.player.load();
    elements.playerDialog.showModal();
}

async function loadServices() {
    const response = await api('/api/services');
    if (!response.ok) throw new Error('Streaming services could not be loaded.');
    state.services = await response.json();
    renderServices();
}

async function loadMedia(force = false) {
    elements.libraryStatus.textContent = force ? 'Rescanning your external drive…' : 'Scanning your library…';
    const response = await api(force ? '/api/media/rescan' : '/api/media', force ? { method: 'POST' } : {});
    if (!response.ok) throw new Error('The local library could not be loaded.');
    const data = await response.json();
    state.media = data.items;
    if (!data.rootAvailable) {
        elements.libraryStatus.textContent = 'The media drive is not mounted or readable.';
    } else {
        const warning = data.warnings.length ? ` ${data.warnings.join(' ')}` : '';
        elements.libraryStatus.textContent = `${data.items.length} title${data.items.length === 1 ? '' : 's'} indexed.${warning}`;
    }
    renderMedia();
}

document.getElementById('account-form').addEventListener('submit', async event => {
    event.preventDefault();
    const error = document.getElementById('account-error');
    error.hidden = true;
    const response = await api('/api/accounts', {
        method: 'POST',
        body: JSON.stringify({
            serviceId: document.getElementById('account-service').value,
            label: document.getElementById('account-label').value,
            username: document.getElementById('account-username').value,
            password: document.getElementById('account-password').value
        })
    });
    if (!response.ok) {
        error.textContent = await readProblem(response, 'The account could not be saved.');
        error.hidden = false;
        return;
    }
    elements.accountDialog.close();
    await loadServices();
    showToast('Account encrypted and saved.');
});

document.getElementById('reveal-form').addEventListener('submit', async event => {
    event.preventDefault();
    const id = document.getElementById('reveal-account-id').value;
    const error = document.getElementById('reveal-error');
    error.hidden = true;
    const response = await api(`/api/accounts/${id}/reveal`, {
        method: 'POST',
        body: JSON.stringify({ adminPassword: document.getElementById('reveal-admin-password').value })
    });
    if (!response.ok) {
        error.textContent = await readProblem(response, 'The credential could not be revealed.');
        error.hidden = false;
        return;
    }
    const credential = await response.json();
    document.getElementById('revealed-username').value = credential.username;
    document.getElementById('revealed-password').value = credential.password;
    document.getElementById('revealed-fields').hidden = false;
    document.getElementById('reveal-submit').hidden = true;
    document.getElementById('reveal-admin-password').value = '';
});

document.getElementById('add-account-button').addEventListener('click', () => openAccountDialog());
document.getElementById('rescan-button').addEventListener('click', () => loadMedia(true).catch(error => showToast(error.message)));
elements.mediaSearch.addEventListener('input', renderMedia);
document.getElementById('logout-button').addEventListener('click', async () => {
    await api('/api/auth/logout', { method: 'POST' });
    window.location.replace('/login');
});

document.querySelectorAll('[data-close-dialog]').forEach(control => {
    control.addEventListener('click', () => {
        const dialog = document.getElementById(control.dataset.closeDialog);
        dialog.close();
    });
});

elements.playerDialog.addEventListener('close', () => {
    elements.player.pause();
    elements.player.removeAttribute('src');
    elements.player.load();
});

elements.accountDialog.addEventListener('close', () => {
    document.getElementById('account-form').reset();
    document.getElementById('account-password').value = '';
});

elements.revealDialog.addEventListener('close', () => {
    document.getElementById('reveal-admin-password').value = '';
    document.getElementById('revealed-username').value = '';
    document.getElementById('revealed-password').value = '';
    document.getElementById('revealed-fields').hidden = true;
});

for (const dialog of document.querySelectorAll('dialog')) {
    dialog.addEventListener('click', event => {
        if (event.target === dialog) dialog.close();
    });
}

async function initialize() {
    const sessionResponse = await fetch('/api/auth/session', { cache: 'no-store' });
    const session = await sessionResponse.json();
    if (!session.authenticated) {
        window.location.replace('/login');
        return;
    }
    state.csrfToken = session.csrfToken;
    document.getElementById('session-user').textContent = session.username;
    await Promise.all([loadServices(), loadMedia()]);
}

initialize().catch(error => showToast(error.message || 'The portal could not be loaded.'));
