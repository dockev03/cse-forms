// Utilitaires communs à la page du formulaire public (app.mjs) et au cadre de l'aperçu (preview.mjs).

/** Logo du CSE, demandé seulement s'il est défini ; l'en-tête reste masqué tant qu'il n'est pas chargé. */
export function showLogo() {
  const brand = document.querySelector('.brand');
  if (!brand) return;
  const logo = document.createElement('img');
  logo.className = 'cse-logo';
  logo.alt = 'Logo du CSE';
  logo.addEventListener('load', () => { brand.hidden = false; }, { once: true });
  logo.src = '/logo';
  brand.replaceChildren(logo);
}

/** Lien vers la notice RGPD complète, ouvert dans un nouvel onglet pour ne pas perdre la saisie. */
export function showPrivacyLink() {
  const footer = document.querySelector('.legal');
  if (!footer) return;
  const link = document.createElement('a');
  link.href = '/donnees-personnelles';
  link.target = '_blank';
  link.rel = 'noopener';
  link.textContent = 'Vos données personnelles (nouvel onglet)';
  footer.replaceChildren(link);
  footer.hidden = false;
}

export function showMessage(root, title, text) {
  const heading = document.createElement('h1');
  heading.textContent = title;
  const paragraph = document.createElement('p');
  paragraph.textContent = text;
  root.replaceChildren(heading, paragraph);
}

export async function requestJson(url, options = {}) {
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
