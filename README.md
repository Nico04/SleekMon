# SleekMon

Moniteur matériel pour le petit écran USB-C **Turing Smart Screen 3.5"** (révision A), sous Windows.
Affiche CPU, GPU, RAM, boîtier et le processus le plus actif, avec des couleurs d'état, et éteint l'écran quand le PC est au repos.

![Maquette v3 — en jeu](.github/images/dashboard.png)

> **Statut : en conception.** Le design (v3) est validé, le code n'est pas encore écrit.

## Fonctionnalités prévues

- Tableau de bord 480×320 paysage, dessiné avec **SkiaSharp** (pas de moteur de thèmes YAML).
- Métriques : CPU % et °C, GPU % et °C, VRAM, RAM, température et ventilateur du boîtier, ventilateurs CPU/GPU, heure. Plus tard : FPS (en jeu) et processus le plus actif (CPU % + mémoire).
- Couleurs d'état : gris (repos), blanc (normal), orange (élevé), rouge (critique). Ventilateur à l'arrêt (0 RPM) en vert.
- **Veille automatique** : écran éteint quand CPU < 10 % et GPU < 5 % pendant 30 s, rallumé dès que la charge remonte.
- Envoi **uniquement des zones modifiées** (tuiles) vers l'écran : c'est le transfert USB qui coûte, pas le dessin.
- Icône dans la barre des tâches : activer/désactiver, mode économie, luminosité (4 paliers), quitter.
- Démarrage automatique avec droits admin (Planificateur de tâches).

## Matériel cible

| | |
|---|---|
| Écran | Turing Smart Screen 3.5" IPS, révision A — USB `VID_1A86&PID_5722`, n° de série `USB35INCHIPSV2`, 320×480 natif (utilisé en 480×320 paysage). |
| Système | Windows 11 x64 |

## Prérequis

- **.NET 10 SDK** (`net10.0-windows`, x64).
- **PawnIO** installé : pilote noyau utilisé par LibreHardwareMonitor ≥ 0.9.6 pour la température CPU AMD et les ventilateurs de la carte mère. Sans lui, ces valeurs s'affichent `--`.
  LibreHardwareMonitor ne l'installe pas tout seul (il le détecte via le registre ; l'appli LibreHardwareMonitor propose de l'installer, version ≥ 2.0).
- **Droits administrateur** pour les capteurs ci-dessus. L'appli se lance aussi sans (elle affiche alors « Limited sensors ») ; le démarrage automatique par le Planificateur de tâches lui donne les droits.
- Fermer toute autre appli qui utilise l'écran (Rizplay, USBMonitor.exe, turing-smart-screen-python…) : le port COM est exclusif.

## Structure prévue

```
SleekMon/
├─ SleekMon.sln
├─ src/SleekMon/
│  ├─ Display/            pilote révision A maison (détection du port, commandes, envoi par tuiles)
│  ├─ Sensors/            ISensorProvider + implémentation LibreHardwareMonitor
│  ├─ Render/             dessin SkiaSharp de la v3 à partir d'un SensorSnapshot (aucune dépendance matérielle)
│  ├─ App/                WinForms ApplicationContext : boucle, veille, icône, commandes
│  └─ assets/fonts/       Saira Semi Condensed (TTF, licence OFL)
└─ .github/images/        images du README
```

## Commandes prévues

```powershell
dotnet build -c Release
dotnet run --project src/SleekMon                             # appli normale (icône dans la barre des tâches)
dotnet run --project src/SleekMon -- --render gaming out.png   # rend un scénario simulé en PNG, sans écran
dotnet run --project src/SleekMon -- --show out.png            # affiche un PNG 480×320 sur l'écran
dotnet run --project src/SleekMon -- --test-pattern            # mire de test (orientation, alignement, couleurs)
dotnet run --project src/SleekMon -- --dump-sensors            # écrit sensors.txt : tous les capteurs (lancer en admin)
```

## Crédits et licences

- Protocole de l'écran : documenté par [turing-smart-screen-python](https://github.com/mathoudebine/turing-smart-screen-python) (GPL-3.0). **Aucun code n'en est recopié** : le pilote est réécrit à partir des faits du protocole.
- [usausa/turing-smart-screen](https://github.com/usausa/turing-smart-screen) (MIT) : référence C# pour l'encodage des commandes.
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0) : capteurs, via le paquet NuGet `LibreHardwareMonitorLib`.
- Police [Saira Semi Condensed](https://fonts.google.com/specimen/Saira+Semi+Condensed) (SIL OFL 1.1).
