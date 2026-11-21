export function ItemIcon({ src }: { src: string | null | undefined }) {
  if (!src) return <div className="item-icon placeholder" />;
  return <img className="item-icon" src={src} alt="" loading="lazy" referrerPolicy="no-referrer" />;
}
