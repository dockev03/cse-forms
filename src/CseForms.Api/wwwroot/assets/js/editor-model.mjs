// Modèle de l'éditeur visuel : opérations sur le fichier de formulaire (objet JSON), sans DOM.
// L'éditeur ne contrôle rien : il produit le fichier, que le serveur contrôle comme un import
// (FormDraftService.SaveAsync). Les objets sont modifiés en place, si bien qu'une propriété que
// l'éditeur ne connaît pas est conservée telle quelle. Exécuté aussi par Node pour les tests.

/** Types de champs proposés, dans l'ordre du menu, avec leur libellé et leurs propriétés propres. */
export const FIELD_TYPES = [
  { type: 'text', label: 'Texte court', props: ['minLength', 'maxLength', 'pattern', 'transform'] },
  { type: 'textarea', label: 'Texte long', props: ['minLength', 'maxLength', 'pattern', 'transform'] },
  { type: 'email', label: 'Email', props: [] },
  { type: 'phone', label: 'Téléphone', props: [] },
  { type: 'date', label: 'Date', props: ['rules'] },
  { type: 'choice', label: 'Choix unique', props: ['options', 'display'] },
  { type: 'multichoice', label: 'Choix multiple', props: ['options', 'min', 'max'] },
  { type: 'site', label: 'Lieu de travail (site)', props: [] },
  { type: 'address', label: 'Adresse', props: [] },
  { type: 'repeat', label: 'Groupe répétable', props: ['fields', 'min', 'max', 'addLabel'] },
  { type: 'consent', label: 'Consentement (case à cocher)', props: [] },
  { type: 'info', label: "Texte d'information", props: ['text'] },
  { type: 'hidden', label: 'Champ caché', props: ['options'] },
];

/** Propriétés valables pour tous les types (la règle sameAs vaut aussi hors des dates). */
const COMMON_PROPS = ['id', 'type', 'label', 'help', 'placeholder', 'required', 'visibleIf', 'rules', 'prefill', 'persist'];

export const CONDITION_OPERATORS = [
  { op: 'equals', label: 'est égal à', value: 'single' },
  { op: 'notEquals', label: 'est différent de', value: 'single' },
  { op: 'in', label: "est l'un de", value: 'list' },
  { op: 'notIn', label: "n'est aucun de", value: 'list' },
  { op: 'notEmpty', label: 'est renseigné', value: 'none' },
  { op: 'empty', label: "n'est pas renseigné", value: 'none' },
];

export const RULE_OPERATORS = [
  { op: 'after', label: 'strictement après', target: 'date' },
  { op: 'before', label: 'strictement avant', target: 'date' },
  { op: 'notBefore', label: 'au plus tôt le', target: 'date' },
  { op: 'notAfter', label: 'au plus tard le', target: 'date' },
  { op: 'past', label: 'dans le passé', target: 'none' },
  { op: 'future', label: 'dans le futur', target: 'none' },
  { op: 'sameAs', label: 'identique au champ', target: 'field' },
];

const MAX_ID_LENGTH = 64;

export function typeInfo(type) {
  return FIELD_TYPES.find((t) => t.type === type);
}

export function typeLabel(type) {
  return typeInfo(type)?.label ?? type;
}

export const hasOptions = (field) => Array.isArray(field?.options);

// ---------------------------------------------------------------------------
// Parcours

/** Champs de premier niveau, dans l'ordre. */
export function topLevelFields(doc) {
  return (doc.sections ?? []).flatMap((s) => (Array.isArray(s?.fields) ? s.fields : []));
}

/** Tous les champs, y compris ceux des groupes répétables. */
export function allFields(doc) {
  return topLevelFields(doc).flatMap((f) => [f, ...(f?.type === 'repeat' && Array.isArray(f.fields) ? f.fields : [])]);
}

/** Groupe répétable qui contient le champ, ou null pour un champ de premier niveau. */
export function parentRepeat(doc, field) {
  return topLevelFields(doc).find((f) => f?.type === 'repeat' && Array.isArray(f.fields) && f.fields.includes(field)) ?? null;
}

/** Section qui contient le champ (ou son groupe répétable). */
export function sectionOf(doc, field) {
  const owner = parentRepeat(doc, field) ?? field;
  return (doc.sections ?? []).find((s) => Array.isArray(s?.fields) && s.fields.includes(owner)) ?? null;
}

/** Liste qui contient le nœud (section ou champ) : <c>doc.sections</c>, <c>section.fields</c> ou <c>repeat.fields</c>. */
export function containerOf(doc, node) {
  if ((doc.sections ?? []).includes(node)) return doc.sections;
  const repeat = parentRepeat(doc, node);
  if (repeat) return repeat.fields;
  return sectionOf(doc, node)?.fields ?? null;
}

// ---------------------------------------------------------------------------
// Identifiants

/** Identifiant proposé à partir d'un libellé : « Date de naissance » donne « dateDeNaissance ». */
export function idFromLabel(label) {
  const words = String(label ?? '')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^A-Za-z0-9]+/g, ' ')
    .trim()
    .split(' ')
    .filter(Boolean);
  let id = words
    .map((w, i) => (i === 0 ? w.toLowerCase() : w[0].toUpperCase() + w.slice(1).toLowerCase()))
    .join('');
  if (!/^[A-Za-z]/.test(id)) id = `champ${id}`;
  return id.slice(0, MAX_ID_LENGTH);
}

function usedIds(doc) {
  return new Set([...(doc.sections ?? []).map((s) => s?.id), ...allFields(doc).map((f) => f?.id)].filter(Boolean));
}

/** Identifiant libre dans tout le formulaire (sections et champs), à partir de <paramref name="base"/>. */
export function uniqueId(doc, base, except = null) {
  const used = usedIds(doc);
  if (except) used.delete(except);
  const root = (base && /^[A-Za-z]/.test(base) ? base : 'champ').slice(0, MAX_ID_LENGTH - 4);
  if (!used.has(root)) return root;
  for (let i = 2; ; i++) {
    if (!used.has(`${root}${i}`)) return `${root}${i}`;
  }
}

// ---------------------------------------------------------------------------
// Création, déplacement, suppression

/** Nouveau champ du type donné, avec un identifiant libre et des valeurs de départ. */
export function createField(doc, type) {
  const field = { id: uniqueId(doc, type === 'info' ? 'information' : 'champ'), type };
  switch (type) {
    case 'info':
      field.text = "Texte à afficher";
      break;
    case 'hidden':
      field.options = ['valeur'];
      field.prefill = 'url';
      break;
    case 'choice':
    case 'multichoice':
      field.label = 'Nouvelle question';
      field.options = ['Option 1', 'Option 2'];
      break;
    case 'repeat':
      field.label = 'Nouveau groupe';
      field.fields = [];
      field.fields.push({ id: uniqueId(doc, 'ligne'), type: 'text', label: 'Nouvelle question' });
      break;
    default:
      field.label = 'Nouvelle question';
  }
  return field;
}

export function createSection(doc) {
  const number = (doc.sections?.length ?? 0) + 1;
  return { id: uniqueId(doc, `section${number}`), title: `Section ${number}`, fields: [] };
}

/** Déplace le nœud vers <paramref name="target"/> à la position <paramref name="index"/> (comptée avant le retrait). */
export function moveNode(doc, node, target, index) {
  const source = containerOf(doc, node);
  if (!source || !target) return false;
  if (node?.type === 'repeat' && topLevelFields(doc).some((f) => f?.type === 'repeat' && f.fields === target)) {
    return false; // pas de groupe répétable dans un groupe répétable
  }
  if (node?.type === 'repeat' && node.fields === target) return false;
  const from = source.indexOf(node);
  let to = Math.max(0, Math.min(index, target.length));
  if (source === target && from < to) to -= 1;
  source.splice(from, 1);
  target.splice(to, 0, node);
  return source !== target || from !== to;
}

/** Monte ou descend le nœud d'un cran dans sa liste ; faux s'il est déjà en bout de liste. */
export function shiftNode(doc, node, delta) {
  const list = containerOf(doc, node);
  const from = list?.indexOf(node) ?? -1;
  const to = from + delta;
  if (from < 0 || to < 0 || to >= list.length) return false;
  list.splice(from, 1);
  list.splice(to, 0, node);
  return true;
}

/**
 * Monte ou descend un champ d'un cran ; en bout de section, un champ de premier niveau passe à la fin de la
 * section précédente ou au début de la suivante. Renvoie la liste d'arrivée, ou null si rien n'a bougé.
 */
export function shiftField(doc, field, delta) {
  if (shiftNode(doc, field, delta)) return containerOf(doc, field);
  if (parentRepeat(doc, field)) return null;
  const sections = doc.sections ?? [];
  const next = sections[sections.indexOf(sectionOf(doc, field)) + delta];
  if (!next || !Array.isArray(next.fields)) return null;
  moveNode(doc, field, next.fields, delta < 0 ? next.fields.length : 0);
  return next.fields;
}

export function removeNode(doc, node) {
  const list = containerOf(doc, node);
  const index = list?.indexOf(node) ?? -1;
  if (index >= 0) list.splice(index, 1);
  return index >= 0;
}

/** Copie du champ juste après lui, avec de nouveaux identifiants (y compris pour les champs d'un groupe). */
export function duplicateField(doc, field) {
  const copy = structuredClone(field);
  copy.id = uniqueId(doc, field.id);
  const list = containerOf(doc, field);
  list.splice(list.indexOf(field) + 1, 0, copy);
  if (Array.isArray(copy.fields)) {
    const renamed = new Map();
    for (const child of copy.fields) {
      const id = uniqueId(doc, child.id);
      renamed.set(child.id, id);
      child.id = id;
    }
    // Les conditions entre champs de la copie suivent les nouveaux identifiants.
    for (const child of copy.fields) {
      forEachReference({ sections: [{ fields: [child] }] }, (holder, key) => {
        if (renamed.has(holder[key])) holder[key] = renamed.get(holder[key]);
      });
    }
  }
  return copy;
}

/** Change le type d'un champ et retire les propriétés qui ne valent pas pour le nouveau type. */
export function changeType(doc, field, type) {
  const allowed = new Set([...COMMON_PROPS, ...(typeInfo(type)?.props ?? [])]);
  const known = new Set([...COMMON_PROPS, ...FIELD_TYPES.flatMap((t) => t.props)]);
  for (const key of Object.keys(field)) {
    if (known.has(key) && !allowed.has(key)) delete field[key];
  }
  field.type = type;
  if (Array.isArray(field.rules) && type !== 'date') {
    field.rules = field.rules.filter((r) => r?.op === 'sameAs');
    if (field.rules.length === 0) delete field.rules;
  }
  if (type === 'info') delete field.required;
  const fresh = createField({ sections: [] }, type);
  for (const key of ['options', 'text', 'fields']) {
    if (fresh[key] !== undefined && field[key] === undefined) {
      field[key] = key === 'fields' ? [{ ...fresh.fields[0], id: uniqueId(doc, 'ligne') }] : fresh[key];
    }
  }
  if (type !== 'info' && type !== 'hidden' && !field.label) field.label = 'Nouvelle question';
}

// ---------------------------------------------------------------------------
// Références entre champs (conditions, règles, accusé de réception)

function forEachConditionRef(condition, visit) {
  if (!condition || typeof condition !== 'object') return;
  if (typeof condition.field === 'string') visit(condition, 'field');
  for (const key of ['all', 'any']) {
    if (Array.isArray(condition[key])) condition[key].forEach((c) => forEachConditionRef(c, visit));
  }
}

/** Appelle <paramref name="visit"/>(objet, clé) pour chaque mention d'un identifiant de champ. */
export function forEachReference(doc, visit) {
  for (const section of doc.sections ?? []) {
    forEachConditionRef(section?.visibleIf, visit);
    for (const field of section?.fields ?? []) {
      for (const f of [field, ...(field?.type === 'repeat' && Array.isArray(field.fields) ? field.fields : [])]) {
        forEachConditionRef(f?.visibleIf, visit);
        for (const rule of Array.isArray(f?.rules) ? f.rules : []) {
          if (typeof rule?.field === 'string') visit(rule, 'field');
        }
      }
    }
  }
  if (doc.receipt && typeof doc.receipt.emailField === 'string') visit(doc.receipt, 'emailField');
}

/** Nombre de conditions, règles et accusés qui citent l'identifiant. */
export function countReferences(doc, id) {
  let count = 0;
  forEachReference(doc, (holder, key) => {
    if (holder[key] === id) count++;
  });
  return count;
}

/** Renomme un champ et met à jour les conditions, les règles et l'accusé de réception qui le citent. */
export function renameField(doc, field, newId) {
  const oldId = field.id;
  field.id = newId;
  if (!oldId || oldId === newId) return;
  forEachReference(doc, (holder, key) => {
    if (holder[key] === oldId) holder[key] = newId;
  });
}

// ---------------------------------------------------------------------------
// Conditions d'affichage

/** Champs qu'une condition du nœud peut citer : ceux de la même ligne d'abord, puis ceux du formulaire. */
export function conditionTargets(doc, node) {
  const comparable = (f) => f && f !== node && f.id && f.type !== 'info' && f.type !== 'repeat';
  const repeat = parentRepeat(doc, node);
  const row = repeat ? repeat.fields.filter(comparable) : [];
  return [...row, ...topLevelFields(doc).filter(comparable)];
}

function isLeaf(c) {
  return c && typeof c === 'object' && !Array.isArray(c) && !('all' in c) && !('any' in c);
}

/**
 * Forme éditable d'une condition : aucune, une comparaison, ou une liste de comparaisons combinées
 * par « toutes » (all) ou « au moins une » (any). Toute autre forme (combinaisons imbriquées) est
 * « avancée » et se modifie en JSON.
 */
export function readCondition(condition) {
  if (condition === undefined || condition === null) return { mode: 'none', items: [] };
  if (isLeaf(condition)) return { mode: 'single', items: [condition] };
  for (const mode of ['all', 'any']) {
    const list = condition[mode];
    if (Array.isArray(list) && Object.keys(condition).length === 1 && list.every(isLeaf)) {
      return { mode, items: list };
    }
  }
  return { mode: 'advanced', items: [] };
}

/** Condition à enregistrer, à partir de la forme éditable ; undefined pour « toujours affiché ». */
export function writeCondition(mode, items) {
  if (mode === 'none' || items.length === 0) return undefined;
  if (mode === 'single') return items[0];
  return { [mode]: items };
}

/** Comparaison de départ sur le champ cible : « est égal à » sa première option, ou « est renseigné ». */
export function defaultComparison(target) {
  if (!target) return { field: '', op: 'notEmpty' };
  if (target.type === 'consent') return { field: target.id, op: 'equals', value: true };
  if (hasOptions(target) && target.options.length > 0) return { field: target.id, op: 'equals', value: target.options[0] };
  return { field: target.id, op: 'notEmpty' };
}

/** Ajuste la valeur d'une comparaison à son opérateur (valeur simple, liste ou rien). */
export function setOperator(comparison, op) {
  const kind = CONDITION_OPERATORS.find((o) => o.op === op)?.value;
  const current = comparison.value;
  comparison.op = op;
  if (kind === 'none') {
    delete comparison.value;
  } else if (kind === 'list') {
    comparison.value = Array.isArray(current) ? current : current === undefined ? [] : [current];
  } else {
    comparison.value = Array.isArray(current) ? current[0] ?? '' : current ?? '';
  }
}

/** Résumé lisible d'une condition, pour la liste des champs. */
export function describeCondition(doc, condition) {
  const read = readCondition(condition);
  if (read.mode === 'none') return '';
  if (read.mode === 'advanced') return 'condition avancée';
  const labelOf = (id) => {
    const f = allFields(doc).find((x) => x?.id === id);
    return f?.label || id || '?';
  };
  const show = (v) => (v === true ? 'coché' : v === false ? 'non coché' : `« ${v} »`);
  const parts = read.items.map((c) => {
    const op = CONDITION_OPERATORS.find((o) => o.op === c.op);
    const value = op?.value === 'none' ? '' : Array.isArray(c.value) ? ` ${c.value.map(show).join(', ')}` : ` ${show(c.value)}`;
    return `${labelOf(c.field)} ${op?.label ?? c.op}${value}`;
  });
  return `si ${parts.join(read.mode === 'any' ? ' ou ' : ' et ')}`;
}

// ---------------------------------------------------------------------------
// Erreurs du contrôle serveur

const PATH_STEP = /^\.(sections|fields)\[(\d+)\]/;

/**
 * Élément visé par un chemin d'erreur du contrôle (<c>$.sections[0].fields[2].label</c>) : section ou
 * champ le plus précis, sinon <c>'form'</c> pour les réglages du formulaire.
 */
export function resolvePath(doc, path) {
  if (typeof path !== 'string' || !path.startsWith('$')) return 'form';
  let rest = path.slice(1);
  let node = null;
  let list = null;
  for (;;) {
    const step = PATH_STEP.exec(rest);
    if (!step) break;
    const owner = node ?? doc;
    list = step[1] === 'sections' && !node ? owner.sections : step[1] === 'fields' && node ? node.fields : null;
    const next = Array.isArray(list) ? list[Number(step[2])] : undefined;
    if (!next || typeof next !== 'object') break;
    node = next;
    rest = rest.slice(step[0].length);
  }
  return node ?? 'form';
}

/** Libellé court d'un nœud pour la liste et les annonces. */
export function nodeTitle(node) {
  if (!node || typeof node !== 'object') return '';
  if (Array.isArray(node.fields) && !node.type) return node.title || node.id || 'Section sans titre';
  return node.label || node.text?.slice(0, 60) || node.id || 'Champ sans libellé';
}
