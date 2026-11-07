# King of Music

Juego de pelea 3D para un jugador, siete luchadores inspirados en géneros musicales mexicanos se enfrentan en un torneo de tres rondas; cada victoria elimina el género del rival y la última pelea es contra la jefa final.

## Requisitos

- **Unity 6000.6.6f1** (URP, Input System).
- **Git LFS**: los siete modelos de personaje pesan más de 100 MB cada uno.

```bash
git lfs install
git clone https://github.com/tic-310019-alt/KOM.git
```

Si ya clonaste sin LFS: `git lfs pull`.

## Abrir y ejecutar

1. Abre la carpeta del proyecto con Unity 6.6.
2. Escena `Assets/Scenes/KOM/MainMenu.unity`.
3. Play.

## Controles

| Tecla | Acción |
|---|---|
| `A` / `D` | Caminar |
| Doble `A` / `D` | Correr |
| `W` | Saltar |
| `S` | Agacharse |
| `L` | Bloquear |
| `G` | Golpe rápido |
| `H` | Golpe fuerte |
| `J` | Patada rápida |
| `K` | Patada fuerte |
| Correr + `K` | Patada voladora |
| `S` + `H` | Uppercut |
| `U` | Poder especial |
| `F1` | Referencia de controles |
| `ESC` | Pausa |

## Combos

Repite el mismo botón dentro de una ventana de 0.55 s:

| Secuencia | Combo | Golpes |
|---|---|---|
| `G` `G` `G` | Combo cuádruple | 4 |
| `H` `H` `H` | Combo de uppercut | 5 |
| `J` `J` `J` `J` | Combo tumbado | 7 |

## Estructura

```
Assets/
  Art/            modelos, materiales y texturas (FBX en LFS)
  KOM/Scripts/    código del juego: combate, interfaz, núcleo
  KOM/Shaders/    shaders propios (escenario B/N, aura, impactos)
  KOM/Animation/  clips y controladores
  Resources/      datos JSON, audio, tarjetas, escenarios
  Scenes/KOM/     las seis escenas del juego
Packages/         dependencias de Unity
ProjectSettings/  configuración del proyecto
Installer/        script de Inno Setup para el instalador
```

Índice detallado de archivos en [`FILE_INDEX.md`](FILE_INDEX.md).

## Compilar

- Build Settings → Windows x86_64.
- Escenas incluidas: `MainMenu`, `Story`, `CharacterSelect`, `Map`, `Combat`, `Ending`.
- Instalador: `Installer/KingOfMusic.iss` con Inno Setup 6.
