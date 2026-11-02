// Numeric identifiers are permanent UIDs. Aliases start with a letter so an
// alias can never shadow another account's numeric UID in the login query.
export const isBetaLoginName=value=>typeof value==='string'&&/^[A-Za-z][A-Za-z0-9_]{2,31}$/.test(value);
export const isBetaLoginIdentifier=value=>typeof value==='string'&&
 (/^[1-9][0-9]{0,18}$/.test(value.trim())||isBetaLoginName(value.trim()));
