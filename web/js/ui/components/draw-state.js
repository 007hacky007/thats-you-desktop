// Drawing toolbar state: tool, colour 1..14, size 1..3, undo and busy flags.
// No DOM, imports in Node.

export const COLOURS = [
  { id: 1, name: 'BLACK', rgb: [0, 0, 0] },
  { id: 2, name: 'RED', rgb: [243, 60, 43] },
  { id: 3, name: 'YELLOW', rgb: [255, 235, 4] },
  { id: 4, name: 'BLUE', rgb: [32, 63, 230] },
  { id: 5, name: 'PINK', rgb: [220, 164, 142] },
  { id: 6, name: 'BROWN', rgb: [114, 75, 43] },
  { id: 7, name: 'GREY', rgb: [143, 144, 146] },
  { id: 8, name: 'DARK_PINK', rgb: [183, 100, 167] },
  { id: 9, name: 'GREEN', rgb: [0, 164, 81] },
  { id: 10, name: 'PURPLE', rgb: [103, 45, 135] },
  { id: 11, name: 'ORANGE', rgb: [245, 146, 27] },
  { id: 12, name: 'WHITE', rgb: [255, 255, 255] },
  { id: 13, name: 'KHAKI', rgb: [162, 119, 75] },
  { id: 14, name: 'TURQUOISE', rgb: [20, 218, 255] },
];

export const TOOLS = ['brush', 'eraser', 'spray'];
export const SIZES = [1, 2, 3];

const FACTORS = {
  brush: [0.08, 0.25, 0.55],
  eraser: [0.08, 0.25, 0.55],
  spray: [0.21, 0.35, 0.8],
};

// diameter in output pixels (384x512 drawing)
export function sizeDiameter(tool, size) {
  const f = FACTORS[tool] || FACTORS.brush;
  const i = SIZES.includes(size) ? size - 1 : 1;
  return f[i] * 64;
}

export function colourCss(id) {
  const c = COLOURS.find((x) => x.id === id) || COLOURS[0];
  return `rgb(${c.rgb[0]}, ${c.rgb[1]}, ${c.rgb[2]})`;
}

export function initialDrawState() {
  return { tool: 'brush', colour: 1, size: 2, canUndo: false, isEmpty: true, busy: false };
}

export function drawReducer(state, action) {
  switch (action && action.type) {
    case 'tool':
      if (!TOOLS.includes(action.tool)) return state;
      return { ...state, tool: action.tool };
    case 'colour':
      if (!Number.isInteger(action.colour) || action.colour < 1 || action.colour > 14) return state;
      return { ...state, colour: action.colour, tool: state.tool === 'eraser' ? 'brush' : state.tool };
    case 'size':
      if (!SIZES.includes(action.size)) return state;
      return { ...state, size: action.size };
    case 'surface':
      return { ...state, canUndo: !!action.canUndo, isEmpty: !!action.isEmpty };
    case 'busy':
      return { ...state, busy: !!action.busy };
    default:
      return state;
  }
}

export function toolbarModel(state) {
  const erasing = state.tool === 'eraser';
  return {
    colours: COLOURS.map((c) => ({
      id: c.id, name: c.name, css: colourCss(c.id), testid: `colour-${c.id}`, selected: !erasing && state.colour === c.id,
    })),
    tools: TOOLS.map((t) => ({ id: t, testid: `tool-${t}`, selected: state.tool === t })),
    sizes: SIZES.map((s) => ({
      id: s, testid: `size-${s}`, selected: state.size === s, diameter: sizeDiameter(state.tool, s),
    })),
    coloursEnabled: !state.busy,
    undoEnabled: state.canUndo && !state.busy,
    confirmEnabled: !state.busy,
  };
}

export function applyToSurface(state, surface) {
  if (!surface) return;
  surface.tool = state.tool;
  surface.colour = state.colour;
  surface.size = state.size;
}
