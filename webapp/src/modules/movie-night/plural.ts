export function pluralMembers(n: number): string {
  const m10 = n % 10;
  const m100 = n % 100;
  let word = 'участников';
  if (m10 === 1 && m100 !== 11) word = 'участник';
  else if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) word = 'участника';
  return `${n} ${word}`;
}
