import { sprite } from '@farmverse/shared/gameTheme';
import * as RS from '@radix-ui/react-select';
import { createContext, useContext } from 'react';

/**
 * Where menus portal to. Inside a modal <dialog> they must render in the dialog itself: anything
 * portaled to <body> sits underneath the dialog's top layer.
 */
export const PortalContainer = createContext<HTMLElement | null>(null);

export interface Option {
  value: string;
  label: string;
  /** Game sprite shown before the label (and in the closed select). */
  icon?: string;
}

interface Props {
  value: string;
  onChange: (value: string) => void;
  options: Option[];
  /** Accessible name when there's no visible <label>. */
  label?: string;
  placeholder?: string;
  className?: string;
  size?: 'sm' | 'md';
}

/** Game-styled dropdown (Radix Select: keyboard, typeahead, screen readers handled). */
export function Select({ value, onChange, options, label, placeholder, className = '', size = 'md' }: Props) {
  const container = useContext(PortalContainer);
  return (
    <RS.Root value={value} onValueChange={onChange}>
      <RS.Trigger className={`fv-select ${size} ${className}`} aria-label={label}>
        <RS.Value placeholder={placeholder} />
        <RS.Icon asChild>
          <img className="fv-select-caret" src={sprite('Icon_PictoIcon_Next01')} alt="" />
        </RS.Icon>
      </RS.Trigger>
      <RS.Portal container={container ?? undefined}>
        <RS.Content className="fv-select-menu" position="popper" sideOffset={6} collisionPadding={12}>
          <RS.ScrollUpButton className="fv-select-scroll" data-dir="up">
            <img src={sprite('Icon_PictoIcon_Next01')} alt="" />
          </RS.ScrollUpButton>
          <RS.Viewport className="fv-select-list">
            {options.map((o) => (
              <RS.Item key={o.value} value={o.value} className="fv-select-item">
                <RS.ItemText>
                  <span className="fv-select-label">
                    {o.icon && <img src={sprite(o.icon)} alt="" />}
                    {o.label}
                  </span>
                </RS.ItemText>
              </RS.Item>
            ))}
          </RS.Viewport>
          <RS.ScrollDownButton className="fv-select-scroll" data-dir="down">
            <img src={sprite('Icon_PictoIcon_Next01')} alt="" />
          </RS.ScrollDownButton>
        </RS.Content>
      </RS.Portal>
    </RS.Root>
  );
}
