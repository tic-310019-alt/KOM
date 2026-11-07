# Índice de archivos

Mapa del proyecto por carpetas. Los archivos `.meta` de Unity se omiten.

## Raíz

| Archivo | Qué es |
|---|---|
| `README.md` | Presentación, requisitos, controles y compilación. |
| `FILE_INDEX.md` | Este índice. |
| `LICENSE` | Licencia MIT. |
| `.gitattributes` | Reglas de Git LFS para los modelos de personaje. |
| `.gitignore` | Exclusiones de Unity (`Library`, `Temp`, `Builds`, `Logs`). |
| `Packages/` | Manifiesto y dependencias de Unity. |
| `ProjectSettings/` | Configuración del proyecto (calidad, entrada, escenas, URP). |
| `Installer/` | Script de Inno Setup y recursos gráficos del instalador. |

## Assets/Art

| Ruta | Contenido |
|---|---|
| `Characters/*.fbx` | Los siete modelos de luchador con sus animaciones. **En Git LFS.** |
| `Characters/FirstCommit/*.fbx` | Copia de referencia de los modelos originales, sin modificaciones. |
| `Characters/Materials/` | Materiales de los personajes. |
| `Characters/*.fbm/` | Texturas que acompañan a cada FBX. |
| `Crowd/CrowdSystem/Sprites/` | Hojas de sprites del público; de aquí sale el atlas de la grada. |
| `Props/*.fbx` | Acordeón, guitarra, micrófono y recolectables. |
| `Props/Materials/`, `Props/Textures/` | Materiales y texturas de los props. |
| `Stages/*.fbx` | Los tres escenarios: La Madrugadora, Bellas Artes y Zócalo. |
| `Stages/Materials/`, `Stages/*.fbm/` | Materiales y texturas de escenario. |

## Assets/KOM

| Ruta | Contenido |
|---|---|
| `Scripts/Core/` | Núcleo: sesión, datos, audio, escenas, localización, precarga. |
| `Scripts/Combat/` | Combate: luchador, controlador, HUD, golpes, especiales, escenario. |
| `Scripts/UI/` | Pantallas: menú, selección, mapa, historia, cierre y fábrica de interfaz. |
| `Shaders/` | Shaders propios del juego. |
| `Animation/Clips/` | Clips de animación horneados. |
| `Animation/Clips/InPlace/` | Copias in-place derivadas de los clips originales. |
| `Animation/Controllers/` | Controladores de animación por personaje. |
| `Editor/` | Herramientas de editor: montaje, horneado, validación y capturas. |

### Scripts/Core

| Archivo | Responsabilidad |
|---|---|
| `GameSession.cs` | Estado de la partida: luchador, rivales sorteados, progreso del torneo. |
| `KomData.cs` | Carga y cachea los JSON de `Resources/Data`. |
| `KomAudio.cs` | Fachada de audio y reproductor persistente (música, grada, voces, SFX). |
| `KomAudioDiag.cs` | Medición de la mezcla en tiempo de ejecución. |
| `KomLoc.cs` | Textos en español e inglés. |
| `KomScenes.cs` | Nombres de escena. |
| `KomSettings.cs` | Ajustes de pantalla y calidad guardados en `PlayerPrefs`. |
| `KomPreloader.cs` | Precarga de personajes y recursos con barra de progreso. |
| `KomVisual.cs` | Utilidades de color y materiales. |
| `ScreenFader.cs` | Transiciones entre escenas. |
| `StoryFlow.cs` | Puente entre el juego y la escena de historia. |
| `InputReader.cs` | Lectura del Input System. |
| `FighterControls.cs` | Mapa de teclas y botones de mando por jugador y por modo. |

### Scripts/Combat

| Archivo | Responsabilidad |
|---|---|
| `Fighter.cs` | Estado, movimiento, ataques, guardia, especiales y animación del luchador. |
| `FighterBrain.cs` | Comportamiento del rival controlado por la máquina. |
| `CombatController.cs` | Reglas del asalto: rondas, tiempo, K.O., recolectables y resultado. |
| `CombatHud.cs` | Interfaz de combate: barras, reloj, carteles y resultado. |
| `CombatTuning.cs` | Constantes de balance y tiempos. |
| `AttackData.cs` | Catálogo de ataques, combos y secuencias. |
| `HitFeedback.cs` | Convierte un impacto en cámara, VFX, hitstop y sonido. |
| `HitVfx.cs`, `HitFlashFx.cs`, `HitPostFx.cs`, `ImpactScreenFx.cs` | Capas del impacto. |
| `SpecialFx.cs`, `SpecialFxRuntime.cs`, `SpecialPropFx.cs` | Efectos de los poderes especiales. |
| `SoundWaveFx.cs`, `GroundSlamFx.cs`, `BlockShieldFx.cs` | Efectos concretos de un poder. |
| `KomPropGrip.cs` | Anclaje de props (guitarra, acordeón, micrófono) a la mano. |
| `KomCrowd.cs`, `KomStageCrowd.cs` | Reacción de la grada y construcción del público. |
| `CombatPickup.cs` | Recolectables de vida y súper. |
| `CombatCameraRig.cs` | Cámara del combate. |
| `StageBuilder.cs`, `VisualMood.cs` | Construcción y ambiente del escenario. |
| `KomCombatProbe.cs`, `KomBalanceProbe.cs`, `KomBossProbe.cs`, `KomFeelProbe.cs`, `KomJumpProbe.cs`, `KomCrowdProbe.cs` | Pruebas automáticas de combate. |
| `KomHitCapture.cs`, `KomSpecialCapture.cs`, `KomSpecialDiag.cs`, `KomFx.cs` | Capturas y diagnóstico de efectos. |

### Scripts/UI

| Archivo | Responsabilidad |
|---|---|
| `KomTheme.cs` | Sistema de diseño: colores, medidas y tipografía. |
| `UIFactory.cs` | Primitivas de interfaz (paneles, textos, barras, tarjetas con aura). |
| `MainMenuController.cs` | Menú principal. |
| `CharacterSelectController.cs` | Selección de luchador y revelación del rival. |
| `MapController.cs` | Mapa del torneo. |
| `StoryController.cs` | Escenas narrativas. |
| `EndingController.cs` | Cierre y concierto. |
| `EqualizerUI.cs` | Ecualizador animado del menú. |
| `KomUiMotion.cs` | Curvas de animación de interfaz. |
| `DevMenu.cs` | Menú de desarrollo (solo en editor y builds de depuración). |

### Shaders

| Archivo | Uso |
|---|---|
| `KOMStageBW.shader` | Escenario en blanco y negro. |
| `KOMAuraShell.shader` | Aura de súper sobre copia de la malla. |
| `KOMEnergyOrb.shader`, `KOMChainSilver.shader` | Orbe de plasma y cadena de plata. |
| `KOMImpactGlow.shader`, `KOMImpactScreen.shader`, `KOMHitFlash.shader`, `KOMAdditiveGlow.shader` | Impactos y destellos. |
| `KOMBlockShield.shader`, `KOMPropBW.shader`, `KOMCrowdBW.shader` | Escudo, props y grada. |

### Editor

| Archivo | Responsabilidad |
|---|---|
| `KomSetup.cs` | Montaje de personajes, estados y controladores. |
| `KomStageSetup.cs`, `KomStageGeometry.cs` | Montaje y medición de escenarios. |
| `KomInPlaceBaker.cs`, `KomAnimCleanup.cs`, `KomAnimDiag.cs`, `KomAnimSheet.cs` | Horneado y validación de clips. |
| `KomAssetImport.cs` | Reglas de importación de modelos, texturas y audio. |
| `KomCrowdTools.cs` | Medición de gradas y horneado del público del Zócalo. |
| `KomValidation.cs`, `KomAttackAudit.cs`, `KomSpecialAudit.cs`, `KomComboDiag.cs` | Validaciones y auditorías. |
| `KomPropSetup.cs`, `KomPropLab.cs`, `KomPropStage.cs` | Anclaje y puesta a punto de props. |
| `KomScaleAudit.cs`, `KomShoulderDiag.cs`, `KomTwistDiag.cs` | Diagnóstico de escala y deformación. |
| `KomPreviewTools.cs`, `KomStageShot.cs`, `KomHitShot.cs`, `KomSpecialShot.cs` | Previews y capturas del editor. |
| `KomAppIconSetup.cs`, `KomUiTextureImport.cs`, `KomShaderIncludeSetup.cs` | Icono, texturas de UI e includes de shaders. |

## Assets/Resources

| Ruta | Contenido |
|---|---|
| `Data/characters.json` | Fichas de los siete luchadores: vida, velocidades, poderes. |
| `Data/stages.json` | Los escenarios: encuadre de cámara, luces y música. |
| `Data/tournament.json` | Nodos del mapa y rotación de rivales. |
| `Data/story.json` | Páginas de la historia. |
| `Audio/Music/` | Tema del menú y las tres pistas de combate. |
| `Audio/Ambience/` | Cama de público (normal y final). |
| `Audio/Porras/` | Cantos de la grada. |
| `Audio/Coreos/` | Canción de cada luchador. |
| `Audio/Sfx/` | Golpes, bloqueos, cargas e impactos. |
| `UI/Cards/` | Tarjetas de personaje. |
| `UI/Portraits/` | Retratos de los luchadores. |
| `UI/Map/`, `UI/Story/`, `UI/Cover/`, `UI/Logo/` | Arte de cada pantalla. |
| `UI/Fonts/` | Playfair Display. |
| `Crowd/` | Atlas, material y layout del público. |
| `Stages/` | Prefabs de escenario. |
| `Props/` | Prefabs de props y recolectables. |
| `Characters/` | Prefabs de luchador y sus mallas. |
| `KOMShaders/` | Materiales que usan los shaders propios. |

## Assets/Settings

| Ruta | Contenido |
|---|---|
| `PC_RPAsset.asset`, `PC_Renderer.asset` | Pipeline y renderer de escritorio. |
| `Mobile_RPAsset.asset`, `Mobile_Renderer.asset` | Pipeline y renderer móvil. |
| `DefaultVolumeProfile.asset`, `SampleSceneProfile.asset` | Perfiles de postproceso. |
| `UniversalRenderPipelineGlobalSettings.asset` | Ajustes globales de URP. |

## Assets/Scenes/KOM

| Escena | Contenido |
|---|---|
| `MainMenu.unity` | Menú principal. |
| `Story.unity` | Escena narrativa reutilizable (intro, veto, final, concierto). |
| `CharacterSelect.unity` | Selección y revelación del rival. |
| `Map.unity` | Mapa del torneo. |
| `Combat.unity` | Combate. |
| `Ending.unity` | Cierre. |
