import { Eye, EyeOff } from 'lucide-react'
import { useState } from 'react'

type PasswordFieldProps = Omit<React.InputHTMLAttributes<HTMLInputElement>, 'type'> & {
  label: string
  hint?: string
}

export function PasswordField({ label, hint, id, ...props }: PasswordFieldProps) {
  const [visible, setVisible] = useState(false)
  const inputId = id ?? label.toLowerCase().replaceAll(' ', '-')

  return (
    <label className="field" htmlFor={inputId}>
      <span className="field-label">{label}</span>
      <span className="password-control">
        <input id={inputId} type={visible ? 'text' : 'password'} aria-describedby={hint ? `${inputId}-hint` : undefined} {...props} />
        <button
          className="eye-button"
          type="button"
          aria-label={visible ? 'Hide password' : 'Show password'}
          aria-pressed={visible}
          onClick={() => setVisible((value) => !value)}
        >
          {visible ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
        </button>
      </span>
      {hint && <span className="field-hint" id={`${inputId}-hint`}>{hint}</span>}
    </label>
  )
}
