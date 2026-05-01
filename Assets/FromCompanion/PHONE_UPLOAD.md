# Phone Photo Upload

This flow lets a phone send camera-roll photos directly to the Quest app over the local Wi-Fi network. It does not require a phone app install: the Quest hosts a small web page, the user opens that URL on their phone, enters the headset code, and chooses photos with the phone browser's native picker.

## How To Try It

1. Open a scene that has `M_ServerBootstrap` enabled.
   - `Assets/FromCompanion/Scenes/ServerScene.unity`
   - `Assets/_Scenes/PhonePhotoUpload.unity`
2. Run the scene on Quest or in the editor.
3. The app shows and logs a phone URL like `http://192.168.1.25:8080` and a short code.
4. On the phone, join the same Wi-Fi network and open that URL.
5. Enter the code, choose one or more photos, and upload.
6. Uploaded photos are saved under `Application.persistentDataPath/Uploads`.
7. `M_PhoneUploadToDisplay` shows the newest upload immediately.
8. `M_QuestGalleryController` refreshes the browseable gallery so imported phone photos can be selected again later.

## Notes

- The phone page tries to convert selected photos to JPEG before upload, which helps with iPhone camera-roll formats.
- The headset can freely browse photos after they have been imported. The phone browser still cannot expose the user's full live camera roll without the user choosing files.
- The old raw endpoint still works: `POST /upload-photo` with image bytes. By default, legacy raw uploads can skip the pairing code so existing Unity companion clients do not break.
- Multipart form uploads also work for browser fallback.
- Imported phone photos are app-owned files, so they remain browseable even if the user denies Quest-wide media/gallery permission.
- This is a local-network prototype. If the router blocks peer-to-peer devices, the phone may not reach the Quest IP.
- For production outside a lab Wi-Fi network, use the same phone page idea with a backend/R2 session instead of a Quest-hosted local server.
