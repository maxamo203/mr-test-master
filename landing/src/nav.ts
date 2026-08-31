/**
 * Config del riel lateral. Vive en un módulo aparte (y no dentro del .astro)
 * para que el tipo se pueda importar desde el layout sin depender de que
 * TypeScript resuelva exports de un componente Astro.
 */

export interface RailItem {
  /** ancla dentro de la página, o ruta completa si sale de ella */
  href: string;
  /** etiqueta que se ve al desplegar el riel */
  label: string;
  /** dos dígitos que se ven siempre, aun con el riel colapsado */
  num: string;
}

/** Secciones de la home (index.astro). */
export const homeItems: RailItem[] = [
  { href: '#inicio', label: 'Inicio', num: '01' },
  { href: '#ritual', label: 'El ritual', num: '02' },
  { href: '#como', label: 'Cómo funciona', num: '03' },
  { href: '#descargar', label: 'Descargar', num: '04' },
];

/** Secciones de la página dedicada (como-funciona.astro). */
export const howItems: RailItem[] = [
  { href: '#escaneo', label: 'El escaneo', num: '01' },
  { href: '#ancla', label: 'El ancla', num: '02' },
  { href: '#noche', label: 'La noche', num: '03' },
  { href: '#entidades', label: 'Las entidades', num: '04' },
  { href: '#companeros', label: 'Compañeros', num: '05' },
  { href: '#inmersion', label: 'Inmersión', num: '06' },
];
