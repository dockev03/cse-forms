// Preuve de travail anti-robot (protocole ALTCHA), sans service tiers : trouver n tel que
// SHA-256(sel + n) = défi. Le calcul se fait pendant la saisie, pour ne pas retarder l'envoi.

const BATCH = 256;

/** Empreinte SHA-256 en hexadécimal minuscule (Web Crypto, disponible dans les navigateurs et Node). */
export async function sha256Hex(text) {
  const digest = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('');
}

/**
 * Résout un défi reçu de /api/public/challenge ; renvoie la solution à joindre à la réponse,
 * ou null si aucun nombre ne convient (défi invalide).
 */
export async function solveChallenge({ algorithm, challenge, maxNumber, salt, signature }) {
  if (algorithm !== 'SHA-256') return null;
  for (let start = 0; start <= maxNumber; start += BATCH) {
    const end = Math.min(start + BATCH - 1, maxNumber);
    const numbers = Array.from({ length: end - start + 1 }, (_, i) => start + i);
    const hashes = await Promise.all(numbers.map((n) => sha256Hex(salt + n)));
    const index = hashes.indexOf(challenge);
    if (index >= 0) return { algorithm, challenge, number: numbers[index], salt, signature };
  }
  return null;
}

/**
 * Fournit des preuves prêtes à l'emploi : la suivante est calculée dès que la précédente est prise.
 * @param {() => Promise<object>} fetchChallenge renvoie un défi du serveur
 */
export function createProofSource(fetchChallenge) {
  let pending = null;
  const prepare = () => {
    pending = fetchChallenge().then(solveChallenge);
    // Une erreur ne doit pas rester non gérée en arrière-plan : elle ressortira à la prochaine prise.
    pending.catch(() => {});
    return pending;
  };

  return {
    prepare,
    /** Renvoie une preuve (une seule utilisation) et lance le calcul de la suivante. */
    async take() {
      const current = pending ?? prepare();
      pending = null;
      try {
        return await current;
      } finally {
        prepare();
      }
    },
  };
}
