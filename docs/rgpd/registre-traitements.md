# Registre des traitements : fiches de l'outil de formulaires

Version : projet du 13/11/2026, à valider par le CSE.
Référence : article 30 du RGPD ; structure du modèle de registre de la CNIL.

Ces fiches s'ajoutent au registre du CSE. Elles décrivent ce que l'outil fait réellement (code et [mise-en-service](../mise-en-service.md)) ; les passages `[À COMPLÉTER : …]` relèvent d'une décision du CSE.

Le registre est tenu par finalité, pas par logiciel. Deux choix possibles pour la fiche 1 :

- **Recommandé** : une fiche unique « Gestion des bénéficiaires des ASC », dont l'outil n'est qu'un support de collecte à côté du logiciel CSE ; la fiche ci-dessous en donne la partie « collecte en ligne ».
- Ou une fiche propre à l'outil, comme ci-dessous, si le registre existant ne couvre pas déjà les bénéficiaires ASC.

---

## Fiche 1 : collecte des inscriptions aux activités sociales et culturelles

| Rubrique | Contenu |
| --- | --- |
| Date de création / mise à jour | [À COMPLÉTER : date de validation en plénière] / — |
| Responsable du traitement | CSE NSE, [À COMPLÉTER : adresse], représenté par [À COMPLÉTER : secrétaire du CSE] |
| Référent RGPD | [À COMPLÉTER] (pas de DPO désigné) |
| Nom du traitement | Inscription au CSE en ligne (formulaire `inscription`) |
| Finalité principale | Enregistrer les salariés et leur foyer pour l'accès aux activités sociales et culturelles |
| Sous-finalités | Vérifier l'éligibilité ; constituer le foyer (conjoint, enfants) pour les prestations qui en dépendent ; créer l'accès du salarié au site du CSE ; accuser réception ; suivre le traitement de la fiche (Nouvelle, Saisie, Archivée) |
| Base légale | [À VALIDER] Proposition : exécution de mesures demandées par la personne / fourniture de la prestation (art. 6.1.b). Alternative : intérêt légitime du CSE à gérer les ASC qu'il a la charge d'assurer (art. 6.1.f, art. L. 2312-78 du Code du travail). Le consentement est à écarter : l'inscription conditionne l'accès aux ASC, il ne serait pas libre. La case « J'ai lu » est un accusé de lecture, pas un consentement |
| Personnes concernées | Salariés de l'entreprise (CDI, CDD, apprentis, stagiaires) ; leurs conjoints ; leurs enfants, dont des mineurs |
| Données collectées | Identité : civilité, nom, prénom. Vie professionnelle : type de contrat, date d'embauche, date de fin, lieu de travail. Coordonnées : adresse, email personnel. Conjoint : nom, prénom. Enfants : nom, prénom, date de naissance, sexe. Traitement : numéro de dossier, horodatage, version du formulaire, statut |
| Données sensibles (art. 9) | Aucune. Données de mineurs : vigilance renforcée (minimisation, accès restreint) |
| Champs à justifier (minimisation) | Civilité, sexe des enfants, données du conjoint ([specs § 5.1](../../specs.md#51-exigences-rgpd)) : [À COMPLÉTER : prestation qui justifie chacun, ou suppression] |
| Source des données | Le salarié lui-même, via le formulaire en ligne (QR code du flyer) ; pour le conjoint et les enfants, collecte indirecte par le salarié (art. 14) |
| Destinataires internes | Élus habilités sur le formulaire, par compte nominatif : [À COMPLÉTER : liste des fonctions]. Administrateurs de l'outil pour la maintenance |
| Destinataires externes (sous-traitants) | Hébergeur de l'outil : [À COMPLÉTER : nom, pays]. Éditeur du logiciel de gestion du CSE : [À COMPLÉTER]. Fournisseur SMTP : [À COMPLÉTER] (ne reçoit que le numéro de dossier, le site et l'email du salarié pour l'accusé). Contrat art. 28 à vérifier pour chacun |
| Non-destinataires | Employeur, service RH : aucun accès aux réponses. Aucune cession à des tiers |
| Transferts hors UE | Aucun ([À VÉRIFIER] pour le fournisseur SMTP et l'éditeur du logiciel CSE) |
| Durées de conservation | Réponse et PDF dans l'outil : 180 jours après le premier traitement (passage de « Nouvelle » à un autre statut), puis purge automatique nocturne ; une fiche jamais traitée n'est pas purgée. Sauvegardes : [À COMPLÉTER : 30 jours glissants proposés]. Adresses IP : en mémoire, fenêtre de limitation de débit (10 min au plus), jamais liées à la réponse. Journal d'audit (numéro de dossier seul) : [À COMPLÉTER : durée, voir point ouvert]. Logiciel CSE : [À COMPLÉTER] |
| Mesures de sécurité | Voir ci-dessous |
| Information des personnes | Mention courte dans le formulaire + notice complète ([notice-information.md](notice-information.md)) ; accusé de réception sans donnée saisie |
| Exercice des droits | Contact : [À COMPLÉTER]. Accès, rectification, effacement : traités par un élu habilité depuis le back-office (fiche, PDF, suppression unitaire tracée) ; réponse sous un mois |
| AIPD | Non requise a priori : pas de critère de la liste CNIL cumulé (pas de données sensibles, pas d'évaluation, pas de surveillance, volume limité). Les données de mineurs ne suffisent pas seules. [À VALIDER] |

### Mesures de sécurité

| Domaine | Mesure |
| --- | --- |
| Contrôle d'accès | Comptes nominatifs, rôles (administrateur, éditeur, lecteur) et droits par formulaire ; aucune inscription libre ; mot de passe robuste, provisoire changé à la première connexion ; 2FA TOTP proposée ; session expirée après 30 min d'inactivité |
| Traçabilité | Journal d'audit : connexion, consultation, téléchargement, export, suppression, changement de statut, refus d'accès, purge |
| Chiffrement | HTTPS obligatoire (HSTS) ; PDF chiffrés AES-256 par l'application ; disque du serveur chiffré [À VÉRIFIER] ; sauvegardes chiffrées [À VÉRIFIER] |
| Emails | Aucune donnée personnelle ni pièce jointe : numéro, formulaire, site, date et lien vers le back-office |
| Formulaire public | Code d'accès imprimé sur le flyer (stocké haché), preuve de travail anti-robot auto-hébergée, limitation de débit par IP |
| Exposition | Aucun traceur, aucune ressource tierce, CSP stricte ; PDF hors racine web, servis après authentification et contrôle des droits |
| Journaux techniques | Aucune donnée personnelle ; rotation limitée (5 × 10 Mo par service) |
| Hébergement | France ou UE ; indépendant de l'employeur [À COMPLÉTER selon le choix du CSE] |
| Maintenance | Analyse des dépendances et des images (Trivy) en CI ; mises à jour mensuelles ; revue annuelle des comptes, des droits et de ce registre |

---

## Fiche 2 : gestion des accès au back-office

Traitement secondaire, propre à l'outil : les données des élus qui l'utilisent.

| Rubrique | Contenu |
| --- | --- |
| Finalité | Authentifier les élus, gérer leurs droits, tracer les accès aux réponses |
| Base légale | Intérêt légitime du CSE à sécuriser les données qu'il traite (art. 6.1.f), et obligation de sécurité (art. 32) |
| Personnes concernées | Élus et membres du CSE disposant d'un compte |
| Données | Identifiant, nom affiché, email, rôle et droits, empreinte du mot de passe, secret 2FA, horodatages de connexion, journal d'audit (actions, dossiers consultés) |
| Destinataires | Administrateurs de l'outil ; hébergeur (sous-traitant) |
| Durées | Compte : jusqu'à la fin du mandat ou de l'habilitation, puis désactivation et suppression [À COMPLÉTER : délai]. Journal d'audit : [À COMPLÉTER : 6 mois à 1 an recommandés par la CNIL pour les journaux d'accès] |
| Information | [À COMPLÉTER : note remise à chaque élu à la création de son compte] |

---

## Points ouverts

| Point | Remarque |
| --- | --- |
| ⚠️ Durée du journal d'audit | Aucune purge du journal d'audit n'existe dans le code : il est conservé sans limite. Il ne contient que des numéros de dossier et des identifiants d'élus, mais une durée doit être fixée, puis implémentée. Suppression de données : changement à risque, à décider avant de coder |
| Base légale | À trancher par le bureau (6.1.b ou 6.1.f) ; reporter le choix dans la notice |
| Fiche unique ASC ou fiche propre à l'outil | Voir l'introduction |
| Contrats de sous-traitance | Hébergeur, éditeur du logiciel CSE, fournisseur SMTP |
| Chiffrement de la base | La base PostgreSQL n'est pas chiffrée par l'application ; le chiffrement du disque est donc nécessaire, pas seulement recommandé |
| Revue | Annuelle, avec la revue des comptes ([mise-en-service § 11](../mise-en-service.md#11-exploitation-courante)) |
