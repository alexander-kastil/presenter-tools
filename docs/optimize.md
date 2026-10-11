# Optimize

<table width="100%" border="1" cellpadding="10" cellspacing="0">
<tr>
<td align="left"><a href="../readme.md#features">← Back to Features</a></td>
<td align="center"></td>
<td align="right"><a href="tray-icon.md">→ Next: Tray icon</a></td>
</tr>
</table>

Optimize cleans up your last pen drawing. It sends the drawing as a PNG image to the AI provider you configure, then replaces the rough ink with typed text or clean shapes in the same color. The provider receives the drawing, so do not optimize anything you do not want to share with that provider.

## Set up an API key

Optimize needs an API key. Set it in [Settings](settings.md) under the AI tab.

| Source | When it is used |
| --- | --- |
| API key field in Settings > AI | Used first when it is not empty |
| `DEEPINFRA_API_KEY` environment variable | Used when the API key field is empty |

If no key is found when you click Optimize, the Settings window opens. If you close it without a key, the button shows **No API key**.

The provider must accept OpenAI-style chat completion requests with an image. The default provider is DeepInfra with the model `Qwen/Qwen3-VL-30B-A3B-Instruct`.

## Optimize a drawing

1. Select **Pen** on the pen bar, choose a color and size, and draw.
2. Click **Optimize**. The button shows **Optimizing...** and stays disabled until the request finishes. The request times out after 60 seconds.
3. The rough ink is replaced by the result.

Optimize works on the pen strokes you drew since the last mode change, undo, text commit, clear all, or successful optimize. It works on one screen only: the screen where you drew the most recent stroke. If you draw, undo, or clear while a request is running, the result is discarded and the button shows **Drawing changed**.

## Results

| Recognized as | What you see |
| --- | --- |
| Handwritten text | A text block in the drawing's color, sized to the height of the drawing |
| A drawing | Clean lines, arrows, polylines, rectangles and ellipses in the same color and width |
| Anything else | No change. The button shows **Not recognized** for 2 seconds. |

## Status messages

The button shows a status for 2 seconds after a failed or skipped run. The tooltip shows the detail.

| Button label | Meaning |
| --- | --- |
| Nothing to optimize | There are no pen strokes to optimize. Draw with Pen first. |
| No API key | No key was found in Settings or in `DEEPINFRA_API_KEY`. |
| Timed out | The provider did not answer within 60 seconds. |
| Error | The request failed. A rejected key reports status 401 or 403 and asks you to check Settings. Other HTTP errors report the status code. |
| Not recognized | The drawing is neither text nor a recognizable drawing. |
| Drawing changed | You changed the drawing while the request was running. |

---

<table width="100%" border="1" cellpadding="10" cellspacing="0">
<tr>
<td align="left"><a href="../readme.md#features">← Back to Features</a></td>
<td align="center"><a href="#optimize">↑ Back to top</a></td>
<td align="right"><a href="tray-icon.md">→ Next: Tray icon</a></td>
</tr>
</table>
