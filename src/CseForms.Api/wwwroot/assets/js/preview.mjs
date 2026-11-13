// Point d'entrée du cadre de l'aperçu (/admin/formulaires/{id}/apercu/cadre) : affiche le brouillon avec le
// moteur du formulaire public. L'envoi est désactivé : la saisie est contrôlée, puis rien ne part au serveur.
import { FormView } from './form-view.mjs';
import { requestJson, showLogo, showMessage, showPrivacyLink } from './page.mjs';

const root = document.getElementById('app');

async function start() {
  const formId = window.location.pathname.split('/')[3] ?? '';
  let result;
  try {
    result = await requestJson(`/admin/formulaires/${encodeURIComponent(formId)}/apercu/donnees`);
  } catch {
    showMessage(root, 'Aperçu indisponible', 'Le serveur ne répond pas. Rechargez la page.');
    return;
  }

  if (result.status !== 200 || !result.body?.preview || !result.body.definition) {
    showMessage(root, 'Aperçu indisponible', 'Le brouillon n\'existe plus ou la session a expiré. Rechargez la page.');
    return;
  }

  const form = result.body;
  document.title = `Aperçu : ${form.title}`;
  if (form.logo) showLogo();
  if (form.privacyNotice) showPrivacyLink();

  // Aucun appel réseau : FormView affiche le résultat d'un envoi simulé.
  new FormView(root, form, async () => ({ status: 0, body: { code: 'preview' } })).render();
}

start();
