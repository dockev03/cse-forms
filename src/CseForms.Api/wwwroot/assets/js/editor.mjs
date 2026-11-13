// Éditeur visuel du back-office (/admin/formulaires/nouveau, /admin/formulaires/{id}/editeur).
// Amélioration progressive : sans script, la page présente le fichier de formulaire en JSON dans une zone
// de texte. Avec script, l'éditeur remplace cette zone, puis y réécrit le fichier juste avant l'envoi : le
// serveur reçoit donc exactement ce qu'un import recevrait, et le contrôle de la même façon.
// Aucun style ni script en ligne (CSP) : uniquement des classes définies dans admin.css.
import * as M from './editor-model.mjs';

const form = document.getElementById('editor-form');
const textarea = document.getElementById('package');
const fallback = document.getElementById('json-fallback');
const mount = document.getElementById('editor');

const lockedSlug = mount?.dataset.slug ?? '';
const publishedIds = new Set((mount?.dataset.publishedIds ?? '').split(' ').filter(Boolean));
const originalIds = new WeakMap();
const autoIds = new WeakSet();
const nodeOf = new WeakMap();
const listOf = new WeakMap();

let doc = null;
let selected = 'form';
let errors = new Map();
let dirty = false;
let jsonMode = false;
let dragged = null;
let dropMark = null;

let tree;
let panel;
let status;
let body;
let toggle;

// ---------------------------------------------------------------------------
// Petits outils DOM

function h(tag, attrs = {}, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (value === undefined || value === null || value === false) continue;
    if (key === 'class') el.className = value;
    else if (key.startsWith('on')) el.addEventListener(key.slice(2), value);
    else if (key in el && typeof value !== 'string') el[key] = value;
    else el.setAttribute(key, value === true ? '' : value);
  }
  for (const child of children.flat()) {
    if (child !== null && child !== undefined && child !== false) el.append(child);
  }
  return el;
}

let uid = 0;
const nextId = (prefix) => `${prefix}-${++uid}`;

function announce(message) {
  status.textContent = '';
  // Laisse aux lecteurs d'écran le temps de voir le changement.
  window.setTimeout(() => { status.textContent = message; }, 50);
}

function changed({ tree: redrawTree = true } = {}) {
  dirty = true;
  if (redrawTree) renderTree();
}

function setOrDelete(obj, key, value) {
  if (value === undefined || value === '' || (Array.isArray(value) && value.length === 0)) delete obj[key];
  else obj[key] = value;
}

// ---------------------------------------------------------------------------
// Champs de saisie du panneau

function fieldWrap(id, label, control, help) {
  const helpId = help ? `${id}-aide` : undefined;
  if (helpId) control.setAttribute('aria-describedby', helpId);
  return h('div', { class: 'field' }, h('label', { for: id }, label), help ? h('p', { class: 'help', id: helpId }, help) : null, control);
}

function textProp(obj, key, label, { help, multiline = false, rows = 3, onInput, readOnly = false, maxLength } = {}) {
  const id = nextId('p');
  const control = multiline
    ? h('textarea', { id, rows: String(rows) })
    : h('input', { id, type: 'text', readOnly, maxLength });
  control.value = obj[key] ?? '';
  control.addEventListener('input', () => {
    setOrDelete(obj, key, control.value.trim() === '' ? undefined : control.value);
    onInput?.(control.value);
    changed();
  });
  return fieldWrap(id, label, control, help);
}

function numberProp(obj, key, label, { help, min = 0, max } = {}) {
  const id = nextId('p');
  const control = h('input', { id, type: 'number', inputmode: 'numeric', min: String(min), max: max === undefined ? undefined : String(max), step: '1' });
  control.value = obj[key] ?? '';
  control.classList.add('short');
  control.addEventListener('input', () => {
    const value = Number.parseInt(control.value, 10);
    setOrDelete(obj, key, Number.isNaN(value) ? undefined : value);
    changed({ tree: false });
  });
  return fieldWrap(id, label, control, help);
}

function checkProp(obj, key, label, { help, defaultValue = false, onChange } = {}) {
  const id = nextId('p');
  const control = h('input', { id, type: 'checkbox' });
  control.checked = obj[key] ?? defaultValue;
  control.addEventListener('change', () => {
    setOrDelete(obj, key, control.checked === defaultValue ? undefined : control.checked);
    onChange?.(control.checked);
    changed();
  });
  const helpId = help ? `${id}-aide` : undefined;
  if (helpId) control.setAttribute('aria-describedby', helpId);
  return h('div', { class: 'field checkbox' }, control, h('label', { for: id }, label), help ? h('p', { class: 'help', id: helpId }, help) : null);
}

function select(id, choices, value, focusKey) {
  const control = h('select', { id, 'data-focus': focusKey });
  for (const choice of choices) control.append(h('option', { value: choice.value }, choice.label));
  control.value = value;
  if (control.value !== value && value !== undefined) {
    control.append(h('option', { value }, `${value} (non reconnu)`));
    control.value = value;
  }
  return control;
}

function selectProp(obj, key, label, choices, { help, emptyValue = '', onChange } = {}) {
  const id = nextId('p');
  const control = select(id, choices, obj[key] ?? emptyValue);
  control.addEventListener('change', () => {
    setOrDelete(obj, key, control.value === emptyValue ? undefined : control.value);
    onChange?.(control.value);
    changed();
  });
  return fieldWrap(id, label, control, help);
}

function linesProp(obj, key, label, { help } = {}) {
  const id = nextId('p');
  const control = h('textarea', { id, rows: '5' });
  control.value = (Array.isArray(obj[key]) ? obj[key] : []).join('\n');
  control.addEventListener('input', () => {
    obj[key] = control.value.split('\n').map((line) => line.trim()).filter(Boolean);
    changed();
  });
  return fieldWrap(id, label, control, help ?? 'Une option par ligne.');
}

function button(label, onClick, { cls, aria, disabled = false } = {}) {
  return h('button', { type: 'button', class: cls, 'aria-label': aria, disabled, onclick: onClick }, label);
}

// ---------------------------------------------------------------------------
// Arborescence : sections et champs, glisser-déposer

const typeChoices = (inRepeat) =>
  M.FIELD_TYPES.filter((t) => !(inRepeat && t.type === 'repeat')).map((t) => ({ value: t.type, label: t.label }));

function addControl(list, inRepeat, where) {
  const id = nextId('ajout');
  const control = select(id, typeChoices(inRepeat), 'text');
  return h(
    'div',
    { class: 'tree-add' },
    h('label', { for: id, class: 'visually-hidden' }, `Type du champ à ajouter ${where}`),
    control,
    button('Ajouter un champ', () => {
      const field = M.createField(doc, control.value);
      autoIds.add(field);
      list.push(field);
      selected = field;
      changed();
      renderPanel();
      announce(`${M.typeLabel(field.type)} ajouté ${where}.`);
      focusSelected();
    }),
  );
}

function badges(node) {
  const parts = [];
  if (node.type) parts.push(h('span', { class: 'badge' }, M.typeLabel(node.type)));
  if (node.required) parts.push(h('span', { class: 'badge' }, 'obligatoire'));
  if (node.visibleIf) parts.push(h('span', { class: 'badge condition' }, 'conditionnel'));
  if (node.persist === false) parts.push(h('span', { class: 'badge' }, 'non enregistré'));
  if (errors.has(node)) parts.push(h('span', { class: 'badge error' }, 'à corriger'));
  return parts;
}

function moveButtons(node, title) {
  const isSection = doc.sections.includes(node);
  const move = (delta) => {
    const moved = isSection ? M.shiftNode(doc, node, delta) : M.shiftField(doc, node, delta);
    if (!moved) {
      announce(delta < 0 ? 'Déjà en première position.' : 'Déjà en dernière position.');
      return;
    }
    selected = node;
    changed();
    renderPanel();
    focusSelected(delta < 0 ? 'up' : 'down');
    announce(`${title} déplacé${placeDescription(node)}.`);
  };
  return [
    button('↑', () => move(-1), { cls: 'icon', aria: `Monter « ${title} »` }),
    button('↓', () => move(1), { cls: 'icon', aria: `Descendre « ${title} »` }),
  ];
}

function placeDescription(node) {
  const list = M.containerOf(doc, node);
  if (!list) return '';
  const position = list.indexOf(node) + 1;
  if (list === doc.sections) return `, position ${position} sur ${list.length}`;
  const repeat = M.parentRepeat(doc, node);
  const owner = repeat ? `du groupe « ${M.nodeTitle(repeat)} »` : `de la section « ${M.nodeTitle(M.sectionOf(doc, node))} »`;
  return `, position ${position} sur ${list.length} ${owner}`;
}

function removeWithConfirm(node) {
  const isSection = doc.sections.includes(node);
  const title = M.nodeTitle(node);
  const fields = isSection ? node.fields ?? [] : [node, ...(Array.isArray(node.fields) ? node.fields : [])];
  const refs = fields.reduce((n, f) => n + (f?.id ? M.countReferences(doc, f.id) : 0), 0);
  let message = isSection ? `Supprimer la section « ${title} » et ses ${fields.length} champ(s) ?` : `Supprimer « ${title} » ?`;
  if (refs > 0) message += ` ${refs} condition(s), règle(s) ou accusé de réception y font référence et seront à corriger.`;
  if (!window.confirm(message)) return;
  M.removeNode(doc, node);
  selected = 'form';
  changed();
  renderPanel();
  announce(`« ${title} » supprimé.`);
  tree.querySelector('.tree-form .tree-select')?.focus();
}

function selectButton(node, label) {
  const isCurrent = selected === node;
  return h(
    'button',
    { type: 'button', class: 'tree-select', 'aria-current': isCurrent ? 'true' : undefined, onclick: () => select_(node) },
    h('span', { class: 'tree-title' }, label),
    node === 'form' ? null : h('span', { class: 'tree-meta' }, ...badges(node)),
  );
}

function select_(node) {
  selected = node;
  renderTree();
  renderPanel();
  panel.querySelector('h2')?.focus();
}

function fieldItem(field) {
  const title = M.nodeTitle(field);
  const li = h(
    'li',
    { class: `tree-field${selected === field ? ' selected' : ''}${errors.has(field) ? ' has-error' : ''}`, draggable: 'true' },
    h(
      'div',
      { class: 'tree-row' },
      h('span', { class: 'drag-handle', 'aria-hidden': 'true', title: 'Glisser pour déplacer' }, '⠿'),
      selectButton(field, title),
      h('span', { class: 'tree-actions' }, ...moveButtons(field, title)),
    ),
  );
  nodeOf.set(li, field);
  if (field.type === 'repeat' && Array.isArray(field.fields)) {
    li.append(fieldList(field.fields), addControl(field.fields, true, `au groupe « ${title} »`));
  }
  return li;
}

function fieldList(list) {
  const ul = h('ul', { class: 'tree-fields' }, ...list.map(fieldItem));
  if (list.length === 0) ul.append(h('li', { class: 'tree-empty' }, 'Aucun champ : ajoutez-en un ou déposez-le ici.'));
  listOf.set(ul, list);
  return ul;
}

function renderTree() {
  tree.replaceChildren();
  const formItem = h('li', { class: `tree-form${selected === 'form' ? ' selected' : ''}${errors.has('form') ? ' has-error' : ''}` },
    h('div', { class: 'tree-row' }, selectButton('form', 'Réglages du formulaire'),
      errors.has('form') ? h('span', { class: 'badge error' }, 'à corriger') : null));
  const sections = h('ol', { class: 'tree-sections' });
  for (const section of doc.sections) {
    const title = M.nodeTitle(section);
    const li = h(
      'li',
      { class: `tree-section${selected === section ? ' selected' : ''}${errors.has(section) ? ' has-error' : ''}`, draggable: 'true' },
      h(
        'div',
        { class: 'tree-row' },
        h('span', { class: 'drag-handle', 'aria-hidden': 'true', title: 'Glisser pour déplacer' }, '⠿'),
        selectButton(section, title),
        h('span', { class: 'tree-actions' }, ...moveButtons(section, title)),
      ),
    );
    nodeOf.set(li, section);
    if (Array.isArray(section.fields)) li.append(fieldList(section.fields), addControl(section.fields, false, `à la section « ${title} »`));
    sections.append(li);
  }
  tree.append(
    h('ul', { class: 'tree-root' }, formItem),
    sections,
    h('p', {}, button('Ajouter une section', () => {
      const section = M.createSection(doc);
      doc.sections.push(section);
      selected = section;
      changed();
      renderPanel();
      announce(`Section « ${section.title} » ajoutée.`);
      focusSelected();
    })),
  );
}

function focusSelected(which) {
  const row = [...tree.querySelectorAll('li')].find((li) => nodeOf.get(li) === selected)?.querySelector(':scope > .tree-row');
  const target = which === 'up' ? row?.querySelector('[aria-label^="Monter"]')
    : which === 'down' ? row?.querySelector('[aria-label^="Descendre"]')
      : row?.querySelector('.tree-select');
  target?.focus();
}

function clearDropMark() {
  dropMark?.el.classList.remove('drop-before', 'drop-after', 'drop-into');
  dropMark = null;
}

/** Endroit où déposer l'élément glissé, sous le pointeur ; null si le dépôt n'y est pas permis. */
function dropTarget(event) {
  const draggedEl = [...tree.querySelectorAll('li')].find((li) => nodeOf.get(li) === dragged);
  if (!draggedEl || draggedEl.contains(event.target)) return null;

  if (doc.sections.includes(dragged)) {
    const li = event.target.closest('.tree-section');
    if (!li) return null;
    const rect = li.getBoundingClientRect();
    const after = event.clientY > rect.top + rect.height / 2;
    return { el: li, list: doc.sections, index: doc.sections.indexOf(nodeOf.get(li)) + (after ? 1 : 0), cls: after ? 'drop-after' : 'drop-before' };
  }

  const ul = event.target.closest('.tree-fields');
  const li = event.target.closest('.tree-field');
  let target = null;
  if (li && ul?.contains(li)) {
    const list = listOf.get(li.parentElement);
    const row = li.querySelector(':scope > .tree-row').getBoundingClientRect();
    const after = event.clientY > row.top + row.height / 2;
    target = { el: li, list, index: list.indexOf(nodeOf.get(li)) + (after ? 1 : 0), cls: after ? 'drop-after' : 'drop-before' };
  } else if (ul) {
    target = { el: ul, list: listOf.get(ul), index: listOf.get(ul).length, cls: 'drop-into' };
  } else {
    const section = event.target.closest('.tree-section');
    const sectionNode = section && nodeOf.get(section);
    if (sectionNode && Array.isArray(sectionNode.fields)) target = { el: section, list: sectionNode.fields, index: 0, cls: 'drop-into' };
  }
  if (!target?.list) return null;

  // Pas de groupe répétable dans un groupe répétable.
  const intoRepeat = M.topLevelFields(doc).some((f) => f?.type === 'repeat' && f.fields === target.list);
  if (dragged.type === 'repeat' && intoRepeat) return null;
  return target;
}

function setupDragAndDrop() {
  tree.addEventListener('dragstart', (event) => {
    const li = event.target.closest?.('li[draggable]');
    if (!li) return;
    dragged = nodeOf.get(li);
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', M.nodeTitle(dragged));
    li.classList.add('dragging');
  });
  tree.addEventListener('dragover', (event) => {
    if (!dragged) return;
    const target = dropTarget(event);
    clearDropMark();
    if (!target) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    target.el.classList.add(target.cls);
    dropMark = target;
  });
  tree.addEventListener('dragleave', (event) => {
    if (!tree.contains(event.relatedTarget)) clearDropMark();
  });
  tree.addEventListener('drop', (event) => {
    if (!dragged || !dropMark) return;
    event.preventDefault();
    const node = dragged;
    const target = dropMark;
    clearDropMark();
    dragged = null;
    if (M.moveNode(doc, node, target.list, target.index)) {
      selected = node;
      changed();
      renderPanel();
      announce(`« ${M.nodeTitle(node)} » déplacé${placeDescription(node)}.`);
      focusSelected();
    } else {
      renderTree();
    }
  });
  tree.addEventListener('dragend', () => {
    clearDropMark();
    dragged = null;
    tree.querySelector('.dragging')?.classList.remove('dragging');
  });
}

// ---------------------------------------------------------------------------
// Conditions d'affichage

function valueControl(comparison, target, kind, id) {
  const choices = target?.type === 'consent'
    ? [{ value: true, label: 'coché' }, { value: false, label: 'non coché' }]
    : M.hasOptions(target) ? target.options.map((o) => ({ value: o, label: o })) : null;

  if (kind === 'single') {
    if (choices) {
      const control = h('select', { id });
      choices.forEach((c, i) => control.append(h('option', { value: String(i) }, c.label)));
      let index = choices.findIndex((c) => c.value === comparison.value);
      if (index < 0) {
        control.append(h('option', { value: 'other' }, `${comparison.value ?? ''} (absente des options)`));
        index = 'other';
      }
      control.value = String(index);
      control.addEventListener('change', () => {
        if (control.value !== 'other') comparison.value = choices[Number(control.value)].value;
        changed();
      });
      return control;
    }
    const control = h('input', { id, type: 'text' });
    control.value = comparison.value ?? '';
    control.addEventListener('input', () => { comparison.value = control.value; changed(); });
    return control;
  }

  if (choices) {
    const group = h('div', { id, class: 'choice-list', role: 'group' });
    for (const choice of choices) {
      const boxId = nextId('v');
      const box = h('input', { id: boxId, type: 'checkbox' });
      box.checked = Array.isArray(comparison.value) && comparison.value.includes(choice.value);
      box.addEventListener('change', () => {
        const values = new Set(Array.isArray(comparison.value) ? comparison.value : []);
        if (box.checked) values.add(choice.value);
        else values.delete(choice.value);
        comparison.value = choices.map((c) => c.value).filter((v) => values.has(v))
          .concat([...values].filter((v) => !choices.some((c) => c.value === v)));
        changed();
      });
      group.append(h('div', { class: 'field checkbox' }, box, h('label', { for: boxId }, choice.label)));
    }
    return group;
  }
  const control = h('textarea', { id, rows: '3' });
  control.value = (Array.isArray(comparison.value) ? comparison.value : []).join('\n');
  control.addEventListener('input', () => {
    comparison.value = control.value.split('\n').map((v) => v.trim()).filter(Boolean);
    changed();
  });
  return control;
}

function comparisonRow(holder, node, comparison, items, index, redraw) {
  const targets = M.conditionTargets(doc, node);
  const fieldId = nextId('c');
  const fieldChoices = targets.map((f) => ({ value: f.id, label: `${M.nodeTitle(f)} (${f.id})` }));
  if (fieldChoices.length === 0) fieldChoices.push({ value: '', label: 'Aucun champ disponible' });
  const fieldSelect = select(fieldId, fieldChoices, comparison.field ?? '', `field-${index}`);
  fieldSelect.addEventListener('change', () => {
    const target = targets.find((f) => f.id === fieldSelect.value);
    for (const key of Object.keys(comparison)) delete comparison[key];
    Object.assign(comparison, M.defaultComparison(target));
    changed();
    redraw(`field-${index}`);
  });

  const opId = nextId('c');
  const opSelect = select(opId, M.CONDITION_OPERATORS.map((o) => ({ value: o.op, label: o.label })), comparison.op ?? 'equals', `op-${index}`);
  opSelect.addEventListener('change', () => {
    M.setOperator(comparison, opSelect.value);
    changed();
    redraw(`op-${index}`);
  });

  const kind = M.CONDITION_OPERATORS.find((o) => o.op === comparison.op)?.value ?? 'single';
  const target = targets.find((f) => f.id === comparison.field);
  const valueId = nextId('c');
  const number = items.length > 1 ? ` ${index + 1}` : '';
  return h(
    'div',
    { class: 'condition-row' },
    h('div', { class: 'field' }, h('label', { for: fieldId }, `Champ${number}`), fieldSelect),
    h('div', { class: 'field' }, h('label', { for: opId }, `Comparaison${number}`), opSelect),
    kind === 'none' ? null : h('div', { class: 'field' }, h('label', { for: valueId }, kind === 'list' ? 'Valeurs' : 'Valeur'), valueControl(comparison, target, kind, valueId)),
    button('Retirer', () => {
      items.splice(index, 1);
      changed();
      redraw();
    }, { cls: 'link', aria: `Retirer la condition${number}` }),
  );
}

function conditionEditor(holder, node, what) {
  const box = h('fieldset', { class: 'condition' });

  const draw = (focusKey) => {
    const read = M.readCondition(holder.visibleIf);
    let mode = read.mode;
    const items = read.items;
    const store = () => setOrDelete(holder, 'visibleIf', M.writeCondition(mode, items));
    const modeId = nextId('c');
    const modes = [
      { value: 'none', label: `${what} toujours affiché${what === 'Section' ? 'e' : ''}` },
      { value: 'single', label: 'Affiché si la condition est remplie' },
      { value: 'all', label: 'Affiché si toutes les conditions sont remplies (et)' },
      { value: 'any', label: 'Affiché si au moins une condition est remplie (ou)' },
    ];
    if (mode === 'advanced') modes.push({ value: 'advanced', label: 'Condition avancée (JSON)' });
    const modeSelect = select(modeId, modes, mode, 'mode');
    modeSelect.addEventListener('change', () => {
      if (mode === 'advanced' && !window.confirm('Remplacer la condition avancée ? Elle sera perdue.')) {
        modeSelect.value = 'advanced';
        return;
      }
      const next = modeSelect.value;
      if (next === 'none') items.length = 0;
      if (next !== 'none' && items.length === 0) items.push(M.defaultComparison(M.conditionTargets(doc, node)[0]));
      if (next === 'single') items.length = 1;
      mode = next;
      store();
      changed();
      draw('mode');
    });

    const children = [
      h('legend', {}, 'Affichage conditionnel'),
      h('p', { class: 'help' }, `Un champ masqué n'est ni contrôlé ni enregistré. Un champ masqué compte comme vide dans les conditions qui le citent.`),
      h('div', { class: 'field' }, h('label', { for: modeId }, 'Affichage'), modeSelect),
    ];

    if (mode === 'advanced') {
      const jsonId = nextId('c');
      const area = h('textarea', { id: jsonId, rows: '8', class: 'code', spellcheck: 'false' });
      area.value = JSON.stringify(holder.visibleIf, null, 2);
      const problem = h('p', { class: 'error-text', role: 'alert' });
      area.addEventListener('change', () => {
        try {
          holder.visibleIf = JSON.parse(area.value);
          problem.textContent = '';
          changed();
        } catch {
          problem.textContent = "Ce n'est pas du JSON valide : la condition n'a pas été modifiée.";
        }
      });
      children.push(fieldWrap(jsonId, 'Condition (JSON)', area, 'Combinaisons imbriquées : voir le format de définition.'), problem);
    } else if (mode !== 'none') {
      const redraw = (key) => { store(); draw(key); };
      items.forEach((comparison, index) => children.push(comparisonRow(holder, node, comparison, items, index, redraw)));
      if (mode !== 'single') {
        children.push(button('Ajouter une condition', () => {
          items.push(M.defaultComparison(M.conditionTargets(doc, node)[0]));
          store();
          changed();
          draw();
        }));
      }
    }

    box.replaceChildren(...children);
    if (focusKey) box.querySelector(`[data-focus="${focusKey}"]`)?.focus();
  };

  draw();
  return box;
}

// ---------------------------------------------------------------------------
// Règles de date et de comparaison

function rulesEditor(field) {
  const box = h('fieldset', { class: 'condition' });
  const ops = M.RULE_OPERATORS.filter((r) => field.type === 'date' || r.op === 'sameAs');

  const draw = (focusKey) => {
    const rules = Array.isArray(field.rules) ? field.rules : [];
    const children = [
      h('legend', {}, 'Règles de contrôle'),
      h('p', { class: 'help' }, field.type === 'date'
        ? 'Dates fixes (AAAA-MM-JJ) ou relatives : today, today+6M, today-18Y, today+30D.'
        : 'Par exemple, une confirmation d\'email identique au champ email.'),
    ];
    rules.forEach((rule, index) => {
      const opId = nextId('r');
      const opSelect = select(opId, ops.map((o) => ({ value: o.op, label: o.label })), rule.op, `rule-op-${index}`);
      opSelect.addEventListener('change', () => {
        rule.op = opSelect.value;
        const kind = M.RULE_OPERATORS.find((o) => o.op === rule.op)?.target;
        if (kind === 'none') { delete rule.field; delete rule.value; }
        if (kind === 'field') delete rule.value;
        changed();
        draw(`rule-op-${index}`);
      });
      const kind = M.RULE_OPERATORS.find((o) => o.op === rule.op)?.target;
      const row = [h('div', { class: 'field' }, h('label', { for: opId }, `Règle ${index + 1}`), opSelect)];
      if (kind !== 'none') {
        const sameType = rule.op === 'sameAs' ? field.type : 'date';
        const candidates = [...(M.parentRepeat(doc, field)?.fields ?? []), ...M.topLevelFields(doc)]
          .filter((f) => f !== field && f?.type === sameType && f.id);
        const targetId = nextId('r');
        if (kind === 'date' && rule.field === undefined && rule.value === undefined) rule.value = 'today';
        if (kind === 'field' && rule.field === undefined && candidates.length > 0) rule.field = candidates[0].id;
        const choices = candidates.map((f) => ({ value: `field:${f.id}`, label: `Champ : ${M.nodeTitle(f)} (${f.id})` }));
        if (kind === 'date') choices.unshift({ value: 'value', label: 'Une date' });
        const current = rule.field !== undefined ? `field:${rule.field}` : kind === 'date' ? 'value' : '';
        if (choices.length === 0) choices.push({ value: '', label: 'Aucun champ du même type' });
        const targetSelect = select(targetId, choices, current, `rule-target-${index}`);
        targetSelect.addEventListener('change', () => {
          if (targetSelect.value.startsWith('field:')) {
            rule.field = targetSelect.value.slice(6);
            delete rule.value;
          } else {
            delete rule.field;
            rule.value = rule.value ?? 'today';
          }
          changed();
          draw(`rule-target-${index}`);
        });
        row.push(h('div', { class: 'field' }, h('label', { for: targetId }, 'Comparer à'), targetSelect));
        if (kind === 'date' && rule.field === undefined) row.push(textProp(rule, 'value', 'Date'));
      }
      row.push(button('Retirer', () => {
        rules.splice(index, 1);
        if (rules.length === 0) delete field.rules;
        changed();
        draw();
      }, { cls: 'link', aria: `Retirer la règle ${index + 1}` }));
      children.push(h('div', { class: 'condition-row' }, ...row));
    });
    children.push(button('Ajouter une règle', () => {
      field.rules = rules;
      field.rules.push(field.type === 'date' ? { op: 'notAfter', value: 'today' } : { op: 'sameAs' });
      changed();
      draw();
    }));
    box.replaceChildren(...children);
    if (focusKey) box.querySelector(`[data-focus="${focusKey}"]`)?.focus();
  };

  draw();
  return box;
}

// ---------------------------------------------------------------------------
// Panneau de propriétés

function errorList(node) {
  const messages = errors.get(node);
  if (!messages?.length) return null;
  return h('div', { class: 'summary', role: 'note' },
    h('p', {}, 'À corriger (contrôle du dernier enregistrement) :'),
    h('ul', {}, ...messages.map((m) => h('li', {}, h('code', {}, m.path), ` : ${m.message}`))));
}

function formPanel() {
  const ui = doc.ui && typeof doc.ui === 'object' ? doc.ui : {};
  const keepUi = () => {
    if (Object.keys(ui).length > 0) doc.ui = ui;
    else delete doc.ui;
  };
  const access = doc.access && typeof doc.access === 'object' ? doc.access : (doc.access = { mode: 'public' });
  const emailFields = M.topLevelFields(doc).filter((f) => f?.type === 'email' && f.persist !== false);

  const receiptBox = h('div', {});
  const drawReceipt = () => {
    const children = [checkProp({ on: Boolean(doc.receipt) }, 'on', 'Envoyer un accusé de réception par email', {
      help: "L'accusé ne contient jamais de donnée saisie : numéro de dossier, formulaire, date et heure, message fixe, contact du CSE.",
      onChange: (on) => {
        if (on) doc.receipt = { emailField: emailFields[0]?.id ?? '' };
        else delete doc.receipt;
        drawReceipt();
      },
    })];
    if (doc.receipt) {
      const choices = emailFields.map((f) => ({ value: f.id, label: `${M.nodeTitle(f)} (${f.id})` }));
      if (choices.length === 0) choices.push({ value: '', label: 'Aucun champ email de premier niveau' });
      children.push(
        selectProp(doc.receipt, 'emailField', 'Champ qui reçoit l\'accusé', choices),
        textProp(doc.receipt, 'message', 'Message fixe ajouté à l\'accusé (facultatif)', { multiline: true, maxLength: 1000 }),
      );
    }
    receiptBox.replaceChildren(...children);
  };
  drawReceipt();

  return [
    h('h2', { tabindex: '-1' }, 'Réglages du formulaire'),
    errorList('form'),
    textProp(doc, 'title', 'Titre', { help: 'Affiché en tête du formulaire et dans le back-office.' }),
    textProp(doc, 'slug', 'Adresse publique (/f/…)', {
      readOnly: Boolean(lockedSlug),
      help: lockedSlug
        ? "L'adresse d'un formulaire existant ne change pas : pour une autre adresse, dupliquez le formulaire."
        : 'Lettres minuscules, chiffres et tirets. Elle ne pourra plus changer ensuite.',
    }),
    textProp(doc, 'caseNumberPrefix', 'Préfixe des numéros de dossier', { help: '2 à 6 lettres (DOS par défaut).', maxLength: 6 }),
    numberProp(doc, 'retentionDays', 'Conservation des réponses (jours)', {
      min: 1,
      help: 'Comptée à partir du traitement de la réponse. Vide : aucune purge automatique, à éviter (RGPD).',
    }),
    selectProp(access, 'mode', 'Accès', [
      { value: 'public', label: 'Public : toute personne ayant le lien' },
      { value: 'pin', label: 'Protégé par un code d\'accès' },
    ], { emptyValue: undefined, help: 'Le code d\'accès est demandé à la publication, jamais dans l\'éditeur.' }),
    checkProp(ui, 'multiStep', 'Une section par étape, avec barre de progression', { onChange: keepUi }),
    textProp(ui, 'confirmationText', 'Texte de la page de confirmation', { multiline: true, onInput: keepUi }),
    receiptBox,
    doc.pdfTemplate ? h('p', { class: 'help' }, 'Ce formulaire a un modèle PDF personnalisé : il est conservé tel quel. Un champ qu\'il utilise ne peut être ni supprimé ni renommé.') : null,
  ];
}

function sectionPanel(section) {
  return [
    h('h2', { tabindex: '-1' }, `Section : ${M.nodeTitle(section)}`),
    errorList(section),
    textProp(section, 'title', 'Titre'),
    textProp(section, 'description', 'Description', { multiline: true }),
    idProp(section),
    checkProp(section, 'collapsed', 'Repliée par défaut'),
    conditionEditor(section, section, 'Section'),
    h('div', { class: 'actions' }, button('Supprimer la section', () => removeWithConfirm(section), { cls: 'danger' })),
  ];
}

function idProp(node) {
  const id = nextId('p');
  const control = h('input', { id, type: 'text', maxLength: 64, spellcheck: 'false', autocomplete: 'off' });
  control.value = node.id ?? '';
  control.classList.add('code');
  const isField = Boolean(node.type);
  const published = isField && originalIds.has(node) && publishedIds.has(originalIds.get(node));
  control.addEventListener('change', () => {
    const value = control.value.trim();
    if (!value) {
      control.value = node.id ?? '';
      return;
    }
    autoIds.delete(node);
    if (isField) M.renameField(doc, node, value);
    else node.id = value;
    changed();
  });
  node[idInput] = control;
  return fieldWrap(id, 'Identifiant', control, (isField
    ? 'Lettres, chiffres et _, commence par une lettre. Utilisé dans les exports, le modèle PDF et les conditions (mises à jour automatiquement).'
    : 'Lettres, chiffres et _, commence par une lettre.')
    + (published ? ' Ce champ existe dans la version en ligne : le renommer sépare les nouvelles réponses des anciennes.' : ''));
}

// Lien non énumérable entre un nœud et son champ « Identifiant » : jamais écrit dans le JSON.
const idInput = Symbol('idInput');

function fieldPanel(field) {
  const inRepeat = Boolean(M.parentRepeat(doc, field));
  const type = field.type;
  const typeId = nextId('p');
  const typeSelect = select(typeId, typeChoices(inRepeat), type, 'type');
  typeSelect.addEventListener('change', () => {
    if (type === 'repeat' && Array.isArray(field.fields) && field.fields.length > 0
      && !window.confirm('Changer le type supprime les champs du groupe. Continuer ?')) {
      typeSelect.value = type;
      return;
    }
    M.changeType(doc, field, typeSelect.value);
    changed();
    renderPanel('type');
  });

  const parts = [
    h('h2', { tabindex: '-1' }, M.nodeTitle(field)),
    errorList(field),
    fieldWrap(typeId, 'Type', typeSelect),
    textProp(field, 'label', type === 'info' ? 'Titre (facultatif)' : type === 'hidden' ? 'Libellé (facultatif)' : 'Libellé', {
      onInput: (value) => {
        if (!autoIds.has(field)) return;
        M.renameField(doc, field, M.uniqueId(doc, M.idFromLabel(value), field.id));
        if (field[idInput]) field[idInput].value = field.id;
      },
    }),
    idProp(field),
  ];

  if (type === 'info') parts.push(textProp(field, 'text', 'Texte affiché', { multiline: true, rows: 5 }));
  if (type !== 'info' && type !== 'hidden') {
    parts.push(textProp(field, 'help', 'Aide', { help: 'Affichée sous le libellé.' }));
  }
  if (['text', 'textarea', 'email', 'phone'].includes(type)) {
    parts.push(textProp(field, 'placeholder', 'Exemple affiché dans la zone vide'));
  }
  if (type !== 'info' && type !== 'hidden') {
    parts.push(checkProp(field, 'required', type === 'consent' ? 'Obligatoire (la case doit être cochée)' : 'Obligatoire'));
  }

  if (type === 'text' || type === 'textarea') {
    parts.push(
      numberProp(field, 'minLength', 'Longueur minimale'),
      numberProp(field, 'maxLength', 'Longueur maximale', { min: 1, help: `Par défaut ${type === 'text' ? 200 : 4000} caractères.` }),
      textProp(field, 'pattern', 'Format (expression régulière)', { help: 'Facultatif. Par exemple ^[0-9]{5}$ pour 5 chiffres.' }),
      selectProp(field, 'transform', 'Mise en forme de la saisie', [
        { value: 'none', label: 'Aucune' },
        { value: 'upper', label: 'MAJUSCULES' },
        { value: 'lower', label: 'minuscules' },
        { value: 'capitalize', label: 'Première Lettre En Majuscule' },
      ], { emptyValue: 'none' }),
    );
  }
  if (type === 'choice' || type === 'multichoice') parts.push(linesProp(field, 'options', 'Options'));
  if (type === 'choice') {
    parts.push(selectProp(field, 'display', 'Présentation', [
      { value: '', label: 'Automatique (boutons jusqu\'à 4 options, sinon liste)' },
      { value: 'radio', label: 'Boutons radio' },
      { value: 'select', label: 'Liste déroulante' },
    ]));
  }
  if (type === 'multichoice') {
    parts.push(numberProp(field, 'min', 'Nombre minimal de choix'), numberProp(field, 'max', 'Nombre maximal de choix', { min: 1 }));
  }
  if (type === 'repeat') {
    parts.push(
      textProp(field, 'addLabel', 'Texte du bouton d\'ajout', { help: 'Par exemple « Ajouter un enfant ».' }),
      numberProp(field, 'min', 'Nombre minimal de lignes'),
      numberProp(field, 'max', 'Nombre maximal de lignes', { min: 1, max: 100, help: '100 au plus.' }),
    );
  }
  if (type === 'hidden') {
    parts.push(
      linesProp(field, 'options', 'Valeurs autorisées', { help: 'Une valeur par ligne : seules ces valeurs sont acceptées.' }),
    );
  }
  if (type !== 'info' && type !== 'repeat') {
    parts.push(checkProp(field, 'prefill', 'Préremplissable par l\'adresse du lien (?' + (field.id ?? 'champ') + '=…)', {
      help: 'Par exemple, un QR code par site qui préremplit le lieu de travail.',
      onChange: (on) => setOrDelete(field, 'prefill', on ? 'url' : undefined),
    }));
    parts.push(checkProp(field, 'persist', 'Enregistrer la réponse', {
      defaultValue: true,
      help: 'Décocher pour un champ de contrôle seulement (par exemple une confirmation d\'email).',
    }));
  }
  if (type !== 'info' && type !== 'repeat') parts.push(rulesEditor(field));
  parts.push(conditionEditor(field, field, 'Champ'));

  if (!inRepeat) {
    const moveId = nextId('p');
    const current = M.sectionOf(doc, field);
    const moveSelect = select(moveId, doc.sections.map((s, i) => ({ value: String(i), label: M.nodeTitle(s) })), String(doc.sections.indexOf(current)));
    moveSelect.addEventListener('change', () => {
      const target = doc.sections[Number(moveSelect.value)];
      if (!target || !Array.isArray(target.fields)) return;
      M.moveNode(doc, field, target.fields, target.fields.length);
      changed();
      announce(`« ${M.nodeTitle(field)} » déplacé à la fin de la section « ${M.nodeTitle(target)} ».`);
    });
    parts.push(fieldWrap(moveId, 'Section', moveSelect));
  }

  parts.push(h('div', { class: 'actions' },
    button('Dupliquer', () => {
      const copy = M.duplicateField(doc, field);
      selected = copy;
      changed();
      renderPanel();
      announce(`Copie de « ${M.nodeTitle(field)} » ajoutée.`);
      focusSelected();
    }),
    button('Supprimer le champ', () => removeWithConfirm(field), { cls: 'danger' })));
  return parts;
}

function renderPanel(focusKey) {
  const content = selected === 'form' ? formPanel()
    : doc.sections.includes(selected) ? sectionPanel(selected)
      : fieldPanel(selected);
  panel.replaceChildren(...content.filter(Boolean));
  if (focusKey) panel.querySelector(`[data-focus="${focusKey}"]`)?.focus();
}

// ---------------------------------------------------------------------------
// Démarrage, mode JSON, envoi

function readErrors() {
  errors = new Map();
  for (const item of document.querySelectorAll('#editor-errors li[data-path]')) {
    const path = item.dataset.path;
    const node = M.resolvePath(doc, path);
    const list = errors.get(node) ?? [];
    list.push({ path, message: item.dataset.message ?? '' });
    errors.set(node, list);
    const target = node;
    item.append(' ', button('Afficher', () => select_(target), { cls: 'link', aria: `Afficher l'élément concerné par l'erreur ${path}` }));
  }
}

function parsePackage(text) {
  try {
    const value = JSON.parse(text);
    if (value && typeof value === 'object' && !Array.isArray(value) && Array.isArray(value.sections)
      && value.sections.every((s) => s && typeof s === 'object' && !Array.isArray(s))) {
      for (const section of value.sections) if (!Array.isArray(section.fields)) section.fields = [];
      return value;
    }
  } catch {
    // Le JSON reste modifiable à la main.
  }
  return null;
}

function setJsonMode(on) {
  jsonMode = on;
  fallback.hidden = !on;
  body.hidden = on;
  toggle.textContent = on ? 'Revenir à l\'éditeur visuel' : 'Modifier le JSON (avancé)';
  toggle.setAttribute('aria-pressed', on ? 'true' : 'false');
}

function onToggle() {
  if (!jsonMode) {
    textarea.value = JSON.stringify(doc, null, 2);
    setJsonMode(true);
    textarea.focus();
    return;
  }
  const parsed = parsePackage(textarea.value);
  if (!parsed) {
    announce('Le JSON n\'est pas lisible par l\'éditeur visuel : corrigez-le, ou enregistrez-le tel quel pour voir les erreurs.');
    return;
  }
  doc = parsed;
  errors = new Map();
  selected = 'form';
  setJsonMode(false);
  renderTree();
  renderPanel();
  focusSelected();
}

function start() {
  if (!form || !textarea || !fallback || !mount) return;

  status = h('p', { class: 'visually-hidden', role: 'status', 'aria-live': 'polite' });
  toggle = button('Modifier le JSON (avancé)', onToggle, {});
  toggle.setAttribute('aria-pressed', 'false');
  tree = h('nav', { class: 'editor-tree', 'aria-label': 'Structure du formulaire' });
  panel = h('section', { class: 'editor-panel', 'aria-label': 'Propriétés de l\'élément sélectionné' });
  body = h('div', { class: 'editor-body' },
    h('p', { class: 'help' }, 'Sélectionnez un élément pour modifier ses propriétés. Pour réordonner, glissez-déposez un élément ou utilisez les flèches ↑ et ↓.'),
    h('div', { class: 'editor' }, tree, panel));
  mount.replaceChildren(h('div', { class: 'editor-toolbar' }, toggle), body, status);
  mount.hidden = false;
  textarea.addEventListener('input', () => { dirty = true; });

  doc = parsePackage(textarea.value);
  if (!doc) {
    // Fichier illisible par l'éditeur : il reste modifiable en JSON.
    toggle.disabled = true;
    setJsonMode(true);
    announce('Ce fichier n\'est pas lisible par l\'éditeur visuel : il reste modifiable en JSON.');
    return;
  }

  M.allFields(doc).forEach((f) => originalIds.set(f, f.id));
  readErrors();
  setJsonMode(false);
  setupDragAndDrop();
  renderTree();
  renderPanel();

  form.addEventListener('submit', () => {
    if (!jsonMode) textarea.value = JSON.stringify(doc, null, 2);
    dirty = false;
  });
  window.addEventListener('beforeunload', (event) => {
    if (dirty) event.preventDefault();
  });
}

start();
