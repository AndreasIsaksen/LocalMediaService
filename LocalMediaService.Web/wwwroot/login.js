const form = document.getElementById('login-form');
const error = document.getElementById('login-error');

async function checkSession() {
    const response = await fetch('/api/auth/session', { cache: 'no-store' });
    if (response.ok && (await response.json()).authenticated) {
        window.location.replace('/');
    }
}

form.addEventListener('submit', async event => {
    event.preventDefault();
    error.hidden = true;
    const submit = form.querySelector('button[type="submit"]');
    submit.disabled = true;
    submit.textContent = 'Signing in…';

    try {
        const response = await fetch('/api/auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                username: document.getElementById('username').value,
                password: document.getElementById('password').value,
                rememberMe: document.getElementById('remember-me').checked
            })
        });

        if (!response.ok) {
            error.textContent = response.status === 429
                ? 'Too many attempts. Wait one minute and try again.'
                : 'The username or password was not accepted.';
            error.hidden = false;
            return;
        }

        window.location.replace('/');
    } catch {
        error.textContent = 'The portal could not be reached.';
        error.hidden = false;
    } finally {
        submit.disabled = false;
        submit.textContent = 'Sign in';
    }
});

checkSession().catch(() => {});
