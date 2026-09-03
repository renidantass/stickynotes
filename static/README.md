# Landing page — StickyNotes

Página de vendas do app, 100% estática (sem build, sem dependências).

## Abrir

Abra `index.html` no navegador, ou sirva a pasta:

```powershell
cd static
python -m http.server 8000
# http://localhost:8000
```

## Estrutura

```
static/
├── index.html          # página (copy em pt-BR)
├── css/
│   └── style.css       # visual completo, responsivo
├── js/
│   └── main.js         # menu mobile, reveal, FAQ, ano
├── img/
│   ├── favicon.svg         # ícone post-it
│   ├── app-hero.svg        # screenshot: desktop com deck + notas
│   ├── app-mural.svg       # screenshot: janela "Todas as notas"
│   └── app-nota.svg        # screenshot: nota aberta em close-up
└── README.md
```

## Screenshots

São mockups em SVG fiéis ao app real: paleta de 6 cores
(`#FFE866, #FF9FC6, #8AC6FF, #A8E09C, #D6B8FF, #FFD07A`),
deck pill de 24px na borda, mural com busca + filtros e nota
sem chrome com cantos de 12px.

Para trocar por prints reais, exporte PNGs com o mesmo nome
e atualize o `<img>` no `index.html`.
