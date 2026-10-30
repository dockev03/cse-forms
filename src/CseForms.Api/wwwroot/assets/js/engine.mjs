// Moteur de formulaires côté client : affichage conditionnel, validation et normalisation.
// Portage fidèle de CseForms.Application.FormDefinitions (C#). Le serveur fait foi ; les deux
// moteurs passent les mêmes jeux de tests (tests/fixtures/form-engine).
// Aucune dépendance au DOM : ce module est aussi exécuté par Node pour les tests.

export const DEFAULT_MAX_LENGTH = { text: 200, textarea: 4000, email: 254, phone: 30 };
export const TEXT_DEFAULT_MAX_LENGTH = 200;
export const MAX_REPEAT_ROWS = 100;

const CONTROL = /[\p{Cc}\p{Cf}]/u;
const MULTIPLE_SPACES = /[  ]{2,}| /g;
const EMAIL = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
const PHONE_SEPARATORS = /[\s.\-()]/g;
const PHONE = /^\+?[0-9]{6,15}$/;
const POSTAL_CODE = /^[0-9]{5}$/;
const DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const RELATIVE_DATE = /^today(?:([+-])([0-9]{1,3})([DMY]))?$/;

// ---------------------------------------------------------------------------
// Normalisation du texte

function stripControl(value, keepNewLines) {
  let result = '';
  for (let i = 0; i < value.length; i++) {
    const c = value[i];
    if (c === '\n' && keepNewLines) {
      result += c;
    } else if (c === '\t' || c === '\n') {
      result += ' ';
    } else if (!CONTROL.test(c)) {
      result += c;
    }
  }
  return result;
}

export function singleLine(value) {
  return stripControl(value, false).replace(MULTIPLE_SPACES, ' ').trim();
}

export function multiLine(value) {
  return stripControl(value.replace(/\r\n/g, '\n').replace(/\r/g, '\n'), true).trim();
}

function capitalize(value) {
  let result = '';
  let startOfWord = true;
  for (const c of value) {
    result += startOfWord ? c.toUpperCase() : c.toLowerCase();
    startOfWord = c === ' ' || c === '-' || c === "'" || c === '’' || c === '\n';
  }
  return result;
}

function applyTransform(transform, value) {
  switch (transform) {
    case 'upper': return value.toUpperCase();
    case 'lower': return value.toLowerCase();
    case 'capitalize': return capitalize(value);
    default: return value;
  }
}

// ---------------------------------------------------------------------------
// Dates (AAAA-MM-JJ, sans fuseau)

const pad = (n, size = 2) => String(n).padStart(size, '0');
const formatDate = (y, m, d) => `${pad(y, 4)}-${pad(m)}-${pad(d)}`;
const daysInMonth = (y, m) => new Date(Date.UTC(y, m, 0)).getUTCDate();

export function isValidDate(value) {
  const match = DATE.exec(value ?? '');
  if (!match) return false;
  const [y, m, d] = match.slice(1).map(Number);
  return y >= 1 && m >= 1 && m <= 12 && d >= 1 && d <= daysInMonth(y, m);
}

function addMonths(date, months) {
  const [y, m, d] = date.split('-').map(Number);
  const total = y * 12 + (m - 1) + months;
  const ny = Math.floor(total / 12);
  const nm = (total % 12) + 1;
  return formatDate(ny, nm, Math.min(d, daysInMonth(ny, nm)));
}

function addDays(date, days) {
  const [y, m, d] = date.split('-').map(Number);
  const result = new Date(Date.UTC(y, m - 1, d + days));
  return formatDate(result.getUTCFullYear(), result.getUTCMonth() + 1, result.getUTCDate());
}

/** Date fixe ou relative (today, today+6M, today-18Y, today+30D). */
export function resolveDateExpression(expression, today) {
  if (isValidDate(expression)) return expression;
  const match = RELATIVE_DATE.exec(expression ?? '');
  if (!match) return null;
  if (!match[1]) return today;
  const amount = Number(match[2]) * (match[1] === '-' ? -1 : 1);
  switch (match[3]) {
    case 'D': return addDays(today, amount);
    case 'M': return addMonths(today, amount);
    default: return addMonths(today, amount * 12);
  }
}

/** Date du jour dans le fuseau du CSE. */
export function todayInParis(now = new Date()) {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Paris', year: 'numeric', month: '2-digit', day: '2-digit',
  }).format(now);
}

const displayDate = (date) => date.split('-').reverse().join('/');

// ---------------------------------------------------------------------------
// Messages (identiques au serveur, sans jamais reprendre la valeur saisie)

function message(code, label, detail) {
  switch (code) {
    case 'required': return `« ${label} » est obligatoire.`;
    case 'invalidType': return `« ${label} » : valeur non reconnue.`;
    case 'tooShort': return `« ${label} » doit contenir au moins ${detail} caractères.`;
    case 'tooLong': return `« ${label} » ne doit pas dépasser ${detail} caractères.`;
    case 'invalidFormat': return `« ${label} » n'a pas le format attendu.`;
    case 'invalidEmail': return `« ${label} » n'est pas une adresse email valide.`;
    case 'invalidPhone': return `« ${label} » n'est pas un numéro de téléphone valide.`;
    case 'invalidDate': return `« ${label} » n'est pas une date valide.`;
    case 'invalidPostalCode': return `« ${label} » doit comporter 5 chiffres.`;
    case 'invalidOption':
    case 'duplicateOption': return `« ${label} » : choix non autorisé.`;
    case 'tooFewItems': return `« ${label} » : au moins ${detail} élément(s) attendu(s).`;
    case 'tooManyItems': return `« ${label} » : ${detail} élément(s) au maximum.`;
    case 'mustBeAccepted': return `« ${label} » doit être coché.`;
    case 'rule.after': return `« ${label} » doit être postérieure à ${detail}.`;
    case 'rule.before': return `« ${label} » doit être antérieure à ${detail}.`;
    case 'rule.notBefore': return `« ${label} » ne peut pas être antérieure à ${detail}.`;
    case 'rule.notAfter': return `« ${label} » ne peut pas être postérieure à ${detail}.`;
    case 'rule.past': return `« ${label} » doit être dans le passé.`;
    case 'rule.future': return `« ${label} » doit être dans le futur.`;
    case 'rule.sameAs': return `« ${label} » doit être identique à « ${detail} ».`;
    default: return `« ${label} » est invalide.`;
  }
}

// ---------------------------------------------------------------------------
// Portée : champs du formulaire ou d'une ligne de groupe répétable

const isObject = (v) => v !== null && typeof v === 'object' && !Array.isArray(v);
const own = (object, key) => Object.prototype.hasOwnProperty.call(object, key);
const labelOf = (field) => field.label ?? field.id;

class Scope {
  constructor(definitions, raw, parent, visibilityRule) {
    this.definitions = definitions;
    this.rawValues = raw;
    this.parent = parent;
    this.visibilityRule = visibilityRule ?? (() => true);
    this.visibility = new Map();
    this.normalized = new Map();
  }

  isVisible(id) {
    if (!this.visibility.has(id)) {
      this.visibility.set(id, this.visibilityRule(this.definitions.get(id)));
    }
    return this.visibility.get(id);
  }

  raw(id) {
    return own(this.rawValues, id) ? this.rawValues[id] : undefined;
  }

  visibleRaw(id) {
    if (this.definitions.has(id)) {
      return this.isVisible(id) ? this.raw(id) : undefined;
    }
    return this.parent?.visibleRaw(id);
  }

  lookup(id) {
    if (this.definitions.has(id)) {
      return { definition: this.definitions.get(id), node: this.normalized.get(id) };
    }
    return this.parent?.lookup(id) ?? { definition: undefined, node: undefined };
  }
}

function comparable(value) {
  if (typeof value === 'string') return singleLine(value);
  if (value === true) return 'true';
  if (value === false) return 'false';
  return JSON.stringify(value);
}

function conditionValues(raw) {
  const values = new Set();
  if (raw === undefined) return values;
  for (const item of Array.isArray(raw) ? raw : [raw]) {
    if (item === null || item === false || item === undefined) continue;
    const value = comparable(item);
    if (value.length > 0) values.add(value);
  }
  return values;
}

function evaluate(condition, scope) {
  if (condition.all) return condition.all.every((c) => evaluate(c, scope));
  if (condition.any) return condition.any.some((c) => evaluate(c, scope));

  const values = conditionValues(scope.visibleRaw(condition.field));
  switch (condition.op) {
    case 'empty': return values.size === 0;
    case 'notEmpty': return values.size > 0;
    case 'equals': return values.has(comparable(condition.value));
    case 'notEquals': return !values.has(comparable(condition.value));
    case 'in': return condition.value.some((v) => values.has(comparable(v)));
    default: return !condition.value.some((v) => values.has(comparable(v)));
  }
}

function topLevelScope(definition, input) {
  const sectionOf = new Map();
  const definitions = new Map();
  for (const section of definition.sections) {
    for (const field of section.fields) {
      sectionOf.set(field.id, section);
      definitions.set(field.id, field);
    }
  }

  const sectionVisibility = new Map();
  const scope = new Scope(definitions, input, null);
  scope.isSectionVisible = (section) => {
    if (!sectionVisibility.has(section)) {
      sectionVisibility.set(section, !section.visibleIf || evaluate(section.visibleIf, scope));
    }
    return sectionVisibility.get(section);
  };
  scope.visibilityRule = (field) =>
    scope.isSectionVisible(sectionOf.get(field.id)) && (!field.visibleIf || evaluate(field.visibleIf, scope));
  return scope;
}

function rowScope(repeatField, row, parent) {
  const definitions = new Map(repeatField.fields.map((f) => [f.id, f]));
  const scope = new Scope(definitions, row, parent);
  scope.visibilityRule = (field) => !field.visibleIf || evaluate(field.visibleIf, scope);
  return scope;
}

/**
 * Visibilité courante, pour le rendu : sections, champs de premier niveau, champs des lignes.
 * Mêmes règles que la validation (un champ masqué compte comme vide).
 */
export function computeVisibility(definition, input) {
  const values = isObject(input) ? input : {};
  const top = topLevelScope(definition, values);
  const rows = new Map();

  return {
    section: (section) => top.isSectionVisible(section),
    field: (id) => top.isVisible(id),
    rowField(repeatField, index, subId) {
      const key = `${repeatField.id}[${index}]`;
      if (!rows.has(key)) {
        const list = Array.isArray(values[repeatField.id]) ? values[repeatField.id] : [];
        rows.set(key, rowScope(repeatField, isObject(list[index]) ? list[index] : {}, top));
      }
      return rows.get(key).isVisible(subId);
    },
  };
}

// ---------------------------------------------------------------------------
// Validation

class Run {
  constructor(definition, input, context) {
    this.definition = definition;
    this.context = context;
    this.errors = [];
    this.pendingRules = [];
    this.top = topLevelScope(definition, input);
  }

  execute() {
    const output = {};
    for (const section of this.definition.sections) {
      for (const field of section.fields) {
        this.processField(field, this.top, field.id, output);
      }
    }
    for (const { scope, field, path } of this.pendingRules) {
      this.applyRules(field, scope, path);
    }
    return { errors: this.errors, data: this.errors.length === 0 ? output : null };
  }

  processField(field, scope, path, output) {
    if (!scope.isVisible(field.id)) return;
    const node = this.validateField(field, scope.raw(field.id), path);
    if (node === undefined) return;

    scope.normalized.set(field.id, node);
    if (field.rules?.length) this.pendingRules.push({ scope, field, path });
    if (field.persist !== false) output[field.id] = structuredClone(node);
  }

  validateField(field, raw, path) {
    const label = labelOf(field);
    const present = raw !== undefined && raw !== null;

    switch (field.type) {
      case 'info': return undefined;
      case 'consent': return this.validateConsent(field, raw, present, path, label);
      case 'multichoice': return this.validateMultichoice(field, raw, present, path, label);
      case 'address': return this.validateAddress(field, raw, present, path, label);
      case 'repeat': return this.validateRepeat(field, raw, present, path, label);
      default: break;
    }

    if (present && typeof raw !== 'string') {
      this.error(path, 'invalidType', label);
      return undefined;
    }

    let text = present ? raw : '';
    text = field.type === 'textarea' ? multiLine(text) : singleLine(text);
    text = applyTransform(field.transform, text);

    if (text.length === 0) {
      if (field.required) this.error(path, 'required', label);
      return undefined;
    }

    return this.validateScalar(field, text, path, label);
  }

  validateScalar(field, text, path, label) {
    const maxLength = field.maxLength ?? DEFAULT_MAX_LENGTH[field.type] ?? TEXT_DEFAULT_MAX_LENGTH;
    if (text.length > maxLength) {
      this.error(path, 'tooLong', label, maxLength);
      return undefined;
    }

    switch (field.type) {
      case 'text':
      case 'textarea':
        if (field.minLength != null && text.length < field.minLength) {
          this.error(path, 'tooShort', label, field.minLength);
          return undefined;
        }
        if (field.pattern != null && !new RegExp(field.pattern).test(text)) {
          this.error(path, 'invalidFormat', label);
          return undefined;
        }
        return text;

      case 'email': {
        const email = text.toLowerCase();
        if (!EMAIL.test(email)) {
          this.error(path, 'invalidEmail', label);
          return undefined;
        }
        return email;
      }

      case 'phone': {
        const phone = text.replace(PHONE_SEPARATORS, '');
        if (!PHONE.test(phone)) {
          this.error(path, 'invalidPhone', label);
          return undefined;
        }
        return phone;
      }

      case 'date':
        if (!isValidDate(text)) {
          this.error(path, 'invalidDate', label);
          return undefined;
        }
        return text;

      case 'choice':
      case 'hidden':
        if (!field.options.includes(text)) {
          this.error(path, 'invalidOption', label);
          return undefined;
        }
        return text;

      case 'site':
        if (!this.context.sites.includes(text)) {
          this.error(path, 'invalidOption', label);
          return undefined;
        }
        return text;

      default:
        this.error(path, 'invalidType', label);
        return undefined;
    }
  }

  validateConsent(field, raw, present, path, label) {
    if (present && typeof raw !== 'boolean') {
      this.error(path, 'invalidType', label);
      return undefined;
    }
    const accepted = raw === true;
    if (!accepted && field.required) {
      this.error(path, 'mustBeAccepted', label);
      return undefined;
    }
    return accepted;
  }

  validateMultichoice(field, raw, present, path, label) {
    if (present && !Array.isArray(raw)) {
      this.error(path, 'invalidType', label);
      return undefined;
    }

    const values = [];
    for (const item of present ? raw : []) {
      if (typeof item !== 'string') {
        this.error(path, 'invalidType', label);
        return undefined;
      }
      values.push(singleLine(item));
    }

    if (values.length === 0) {
      if (field.required || field.min > 0) this.error(path, 'required', label);
      return undefined;
    }
    if (values.some((v) => !field.options.includes(v))) {
      this.error(path, 'invalidOption', label);
      return undefined;
    }
    if (new Set(values).size !== values.length) {
      this.error(path, 'duplicateOption', label);
      return undefined;
    }
    if (!this.checkCount(field, values.length, path, label)) return undefined;

    return field.options.filter((o) => values.includes(o));
  }

  validateAddress(field, raw, present, path, label) {
    if (present && !isObject(raw)) {
      this.error(path, 'invalidType', label);
      return undefined;
    }

    const part = (name) => {
      if (!present || !own(raw, name) || raw[name] === null || raw[name] === undefined) return '';
      return typeof raw[name] === 'string' ? singleLine(raw[name]) : null;
    };

    const parts = [['street', 'adresse'], ['postalCode', 'code postal'], ['city', 'ville']]
      .map(([name, partLabel]) => ({ name, label: `${label} : ${partLabel}`, value: part(name) }));

    if (parts.some((p) => p.value === null)) {
      this.error(path, 'invalidType', label);
      return undefined;
    }
    if (parts.every((p) => p.value.length === 0)) {
      if (field.required) this.error(path, 'required', label);
      return undefined;
    }

    // Une adresse commencée doit être complète.
    const result = {};
    let valid = true;
    for (const { name, label: partLabel, value } of parts) {
      const partPath = `${path}.${name}`;
      if (value.length === 0) {
        this.error(partPath, 'required', partLabel);
        valid = false;
      } else if (value.length > TEXT_DEFAULT_MAX_LENGTH) {
        this.error(partPath, 'tooLong', partLabel, TEXT_DEFAULT_MAX_LENGTH);
        valid = false;
      } else if (name === 'postalCode' && !POSTAL_CODE.test(value)) {
        this.error(partPath, 'invalidPostalCode', partLabel);
        valid = false;
      } else {
        result[name] = name === 'city' ? value.toUpperCase() : value;
      }
    }
    return valid ? result : undefined;
  }

  validateRepeat(field, raw, present, path, label) {
    if (present && !Array.isArray(raw)) {
      this.error(path, 'invalidType', label);
      return undefined;
    }

    const rows = present ? raw : [];
    const max = field.max ?? MAX_REPEAT_ROWS;
    if (rows.length > max) {
      this.error(path, 'tooManyItems', label, max);
      return undefined;
    }
    if (rows.length === 0) {
      if (field.required || field.min > 0) this.error(path, 'required', label);
      return undefined;
    }
    if (!this.checkCount(field, rows.length, path, label)) return undefined;

    const result = [];
    const errorsBefore = this.errors.length;
    rows.forEach((row, i) => {
      const rowPath = `${path}[${i}]`;
      if (!isObject(row)) {
        this.error(rowPath, 'invalidType', label);
        return;
      }
      const scope = rowScope(field, row, this.top);
      const rowOutput = {};
      for (const sub of field.fields) {
        this.processField(sub, scope, `${rowPath}.${sub.id}`, rowOutput);
      }
      result.push(rowOutput);
    });

    return this.errors.length === errorsBefore ? result : undefined;
  }

  checkCount(field, count, path, label) {
    if (field.min != null && count < field.min) {
      this.error(path, 'tooFewItems', label, field.min);
      return false;
    }
    if (field.max != null && count > field.max) {
      this.error(path, 'tooManyItems', label, field.max);
      return false;
    }
    return true;
  }

  applyRules(field, scope, path) {
    const label = labelOf(field);
    const node = scope.normalized.get(field.id);

    for (const rule of field.rules) {
      if (rule.op === 'sameAs') {
        const other = scope.lookup(rule.field);
        if (other.node !== undefined && JSON.stringify(other.node) !== JSON.stringify(node)) {
          this.error(path, 'rule.sameAs', label, labelOf(other.definition));
          return;
        }
        continue;
      }

      let target;
      let detail;
      if (rule.op === 'past' || rule.op === 'future') {
        target = this.context.today;
        detail = '';
      } else if (rule.field != null) {
        const other = scope.lookup(rule.field);
        if (other.node === undefined) continue; // Cible masquée, vide ou invalide.
        target = other.node;
        detail = `« ${labelOf(other.definition)} »`;
      } else {
        target = resolveDateExpression(rule.value, this.context.today);
        detail = `au ${displayDate(target)}`;
      }

      let ok;
      switch (rule.op) {
        case 'after':
        case 'future': ok = node > target; break;
        case 'before':
        case 'past': ok = node < target; break;
        case 'notBefore': ok = node >= target; break;
        default: ok = node <= target; break;
      }

      if (!ok) {
        this.error(path, `rule.${rule.op}`, label, detail);
        return;
      }
    }
  }

  error(path, code, label, detail) {
    this.errors.push({ path, code, message: message(code, label, detail) });
  }
}

/**
 * Valide et normalise une saisie.
 * @param {{sections: object[]}} definition définition publiée
 * @param {unknown} input saisie brute
 * @param {{today: string, sites: string[]}} context date du jour (AAAA-MM-JJ, Europe/Paris) et sites actifs
 * @returns {{errors: {path: string, code: string, message: string}[], data: object | null}}
 */
export function validate(definition, input, context) {
  if (!isObject(input)) {
    return { errors: [{ path: '', code: 'invalidType', message: 'Réponse illisible.' }], data: null };
  }
  return new Run(definition, input, context).execute();
}
