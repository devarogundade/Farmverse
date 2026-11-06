# Stageshow kit

The `kit/` folder is copied from the Stageshow skill (`~/.agents/skills/stageshow/kit`, https://benji.org/stageshow, MIT, see the
skill's LICENSE and NOTICE.md). Faux product UI for the landing page and other marketing demos: import from
`@farmverse/shared/stageshow`. Keep it unmodified so it can be refreshed from the skill; restyle parts from the demo's own CSS.

Local change: `kit/phone.tsx` drops an unused `useLayoutEffect` import (our tsconfig has `noUnusedLocals`).
