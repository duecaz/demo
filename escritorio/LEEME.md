# Contador de puntos táctiles — app de escritorio (C# / WPF)

Ventana nativa de Windows: dibujas con los dedos, arriba el número de **puntos
activos** y cada trazo sale de un color distinto, con el trazo suavizado para
que no se vea dentado.

Es la misma app que `trazos.html` de la raíz del repo, pero nativa: WPF entiende
el multitáctil de Windows directamente, sin navegador de por medio.

## Sacar el .exe

Tres caminos, de menos a más instalación:

**1. Descargarlo ya compilado.** Cada cambio en esta carpeta dispara el flujo
*App de escritorio* en la pestaña Actions del repo. Entra en la última
ejecución y baja el artefacto `Trazos-windows-x64` del final de la página
([primera compilación](https://github.com/duecaz/demo/actions/runs/31647884700)).
Es autocontenido: no hace falta instalar .NET, se descomprime y se ejecuta.
Ocupa unos 60 MB porque lleva dentro el runtime de .NET.

**2. Con el SDK de .NET** (`winget install Microsoft.DotNet.SDK.8`):

```
cd escritorio
dotnet run                      # para probarlo
dotnet publish -c Release       # deja el .exe en bin\Release\net8.0-windows\win-x64\publish\
```

**3. Sin instalar nada:** doble clic en `compilar.bat`. Usa el compilador de C#
que Windows ya trae dentro (.NET Framework 4.x) y deja `Trazos.exe` aquí mismo.

## Manejo

| Acción | |
|---|---|
| Dibujar | dedo, lápiz o ratón, varios dedos a la vez |
| Deshacer | botón o `Ctrl+Z` |
| Limpiar | botón o `Supr` |
| Pantalla completa | `F11`, y `Esc` para salir |

## Cómo está hecho

- **Un diccionario `pointerId → trazo`.** Cada dedo lleva su propio trazo, su
  punta suavizada y su color, sin mezclarse. El marcador es el tamaño de ese
  diccionario, así que sube al apoyar y baja al levantar.
- **Easing por fotograma.** En `CompositionTarget.Rendering`, la punta persigue
  al dedo con un factor normalizado al tiempo real del fotograma, de modo que la
  suavidad es la misma a 60 Hz que a 165 Hz.
- **Modo retenido.** Cada tramo cerrado es un `DrawingVisual` propio que WPF
  conserva; el bucle solo repinta la punta. Un trazo largo no cuesta más que uno
  corto.
- **Color por ángulo áureo.** 137,5° de salto en el círculo de color: ni los
  trazos seguidos ni los simultáneos caen en tonos parecidos.
- **Cierre de dedos fantasma.** Si el toque desaparece sin avisar (rechazo de
  palma, la ventana pierde el foco), `LostTouchCapture` y `Deactivated` cierran
  el trazo para que el contador no se quede desfasado.
- Se desactivan *press and hold*, *flicks* y el feedback táctil de Windows: si
  no, mantener el dedo saca el círculo del clic derecho y te corta el trazo.

## Estado

El código está compilado y verificado en CI (runner de Windows). Lo que no está
probado con hardware real es el comportamiento del multitáctil y del rechazo de
palma en la pizarra: eso depende del driver del panel y solo se confirma
apoyando la mano.
