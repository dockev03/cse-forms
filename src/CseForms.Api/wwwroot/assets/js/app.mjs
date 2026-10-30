// Point d'entrée de la page /f/{slug} : charge le formulaire puis l'affiche.
import { FormView } from './form-view.mjs';

const root = document.getElementById('app');

function showMessage(title, text) {
  const heading = document.createElement('h1');
  heading.textContent = title;
  const paragraph = document.createElement('p');
  paragraph.textContent = text;
  root.replaceChildren(heading, paragraph);
}

async function requestJson(url, options = {}) {
  const response = await fetch(url, {
    ...options,
    credentials: 'same-origin',
    headers: { Accept: 'application/json', ...(options.headers ?? {}) },
  });
  let body = null;
  try {
    body = await response.json();
  } catch {
    // Réponse sans corps JSON : seul le statut compte.
  }
  return { status: response.status, body };
}

async function start() {
  const slug = window.location.pathname.split('/')[2] ?? '';
  const api = `/api/public/forms/${encodeURIComponent(decodeURIComponent(slug))}`;

  let result;
  try {
    result = await requestJson(api);
  } catch {
    showMessage('Formulaire indisponible', 'Le serveur ne répond pas. Vérifiez votre connexion puis réessayez.');
    return;
  }

  if (result.status === 404) {
    showMessage('Formulaire introuvable', 'Ce lien ne correspond à aucun formulaire. Vérifiez l\'adresse ou le QR code.');
    return;
  }
  if (result.status !== 200 || !result.body) {
    showMessage('Formulaire indisponible', result.body?.message ?? 'Une erreur est survenue. Réessayez plus tard.');
    return;
  }

  const form = result.body;
  document.title = form.title;
  if (!form.accepting || !form.definition) {
    showMessage(form.title, 'Ce formulaire n\'accepte pas de réponse pour le moment.');
    return;
  }

  const submit = (payload) => requestJson(`${api}/submissions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });

  new FormView(root, form, submit).render();
}

start();
