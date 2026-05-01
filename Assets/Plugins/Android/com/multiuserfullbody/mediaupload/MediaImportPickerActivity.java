package com.multiuserfullbody.mediaupload;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.ContentResolver;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.MediaStore;
import android.provider.OpenableColumns;
import android.util.Log;
import android.webkit.MimeTypeMap;
import android.widget.Toast;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.Locale;

public class MediaImportPickerActivity extends Activity
{
    private static final String TAG = "MediaImportPicker";
    private static final int REQUEST_CODE_PICK_IMAGE = 24061;
    private static final String EXTRA_ALLOW_MULTIPLE_SELECTION = "com.multiuserfullbody.mediaupload.ALLOW_MULTIPLE_SELECTION";
    private static final String EXTRA_MAX_SELECTION_COUNT = "com.multiuserfullbody.mediaupload.MAX_SELECTION_COUNT";
    private static final String ACTION_PICK_IMAGES = "android.provider.action.PICK_IMAGES";
    private static final String EXTRA_PICK_IMAGES_MAX = "android.provider.extra.PICK_IMAGES_MAX";

    public static void launch(Activity activity)
    {
        launch(activity, false, 1);
    }

    public static void launch(Activity activity, boolean allowMultiple, int maxSelectionCount)
    {
        if (activity == null)
            return;

        Intent intent = new Intent(activity, MediaImportPickerActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        intent.putExtra(EXTRA_ALLOW_MULTIPLE_SELECTION, allowMultiple);
        intent.putExtra(EXTRA_MAX_SELECTION_COUNT, Math.max(1, maxSelectionCount));
        activity.startActivity(intent);
    }

    @Override
    protected void onCreate(Bundle savedInstanceState)
    {
        super.onCreate(savedInstanceState);

        try
        {
            boolean allowMultiple = getIntent() != null && getIntent().getBooleanExtra(EXTRA_ALLOW_MULTIPLE_SELECTION, false);
            int maxSelectionCount = getIntent() != null ? getIntent().getIntExtra(EXTRA_MAX_SELECTION_COUNT, 1) : 1;
            Log.i(TAG, "onCreate allowMultiple=" + allowMultiple + ", maxSelectionCount=" + maxSelectionCount);
            Intent pickerIntent = buildPickerIntent(allowMultiple, maxSelectionCount);
            if (pickerIntent == null)
                throw new ActivityNotFoundException("No compatible image picker intent could be resolved.");

            Log.i(TAG, "Starting picker intent: action=" + pickerIntent.getAction() + ", type=" + pickerIntent.getType());
            startActivityForResult(pickerIntent, REQUEST_CODE_PICK_IMAGE);
        }
        catch (ActivityNotFoundException ex)
        {
            Log.w(TAG, "No picker activity was available for image import.", ex);
            Toast.makeText(this, "No compatible image picker was found. Sharing from Gallery is still supported.", Toast.LENGTH_LONG).show();
            finish();
            overridePendingTransition(0, 0);
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data)
    {
        super.onActivityResult(requestCode, resultCode, data);

        if (requestCode != REQUEST_CODE_PICK_IMAGE)
            return;

        Log.i(TAG, "onActivityResult resultCode=" + resultCode + ", hasData=" + (data != null));

        if (resultCode == RESULT_OK && data != null)
            importSelectedImages(data);

        launchUnityActivity();
        finish();
        overridePendingTransition(0, 0);
    }

    private Intent buildPickerIntent(boolean allowMultiple, int maxSelectionCount)
    {
        Intent photoPickerIntent = buildPhotoPickerIntent(allowMultiple, maxSelectionCount);
        if (canResolveIntent(photoPickerIntent))
        {
            Log.i(TAG, "Launching picker via MediaStore.ACTION_PICK_IMAGES.");
            return photoPickerIntent;
        }

        Intent openDocumentIntent = buildOpenDocumentIntent(allowMultiple);
        if (canResolveIntent(openDocumentIntent))
        {
            Log.i(TAG, "Launching picker via Intent.ACTION_OPEN_DOCUMENT.");
            return openDocumentIntent;
        }

        Intent getContentIntent = buildGetContentIntent(allowMultiple);
        if (canResolveIntent(getContentIntent))
        {
            Log.i(TAG, "Launching picker via Intent.ACTION_GET_CONTENT chooser.");
            return Intent.createChooser(getContentIntent, allowMultiple ? "Select photos to sync" : "Select an image");
        }

        Intent pickIntent = buildMediaStorePickIntent();
        if (canResolveIntent(pickIntent))
        {
            Log.i(TAG, "Launching picker via Intent.ACTION_PICK.");
            return pickIntent;
        }

        Log.w(TAG, "No compatible picker intent could be resolved.");
        return null;
    }

    private Intent buildPhotoPickerIntent(boolean allowMultiple, int maxSelectionCount)
    {
        if (!isPhotoPickerAvailable())
            return null;

        Intent intent = new Intent(ACTION_PICK_IMAGES);
        intent.setType("image/*");
        intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[] { "image/*" });

        if (allowMultiple)
            intent.putExtra(EXTRA_PICK_IMAGES_MAX, Math.max(2, maxSelectionCount));

        return intent;
    }

    private Intent buildOpenDocumentIntent(boolean allowMultiple)
    {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("image/*");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        intent.addFlags(Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);

        if (allowMultiple)
            intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);

        return intent;
    }

    private Intent buildGetContentIntent(boolean allowMultiple)
    {
        Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("image/*");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);

        if (allowMultiple)
            intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);

        return intent;
    }

    private Intent buildMediaStorePickIntent()
    {
        Intent intent = new Intent(Intent.ACTION_PICK, MediaStore.Images.Media.EXTERNAL_CONTENT_URI);
        intent.setType("image/*");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        return intent;
    }

    private boolean canResolveIntent(Intent intent)
    {
        if (intent == null)
            return false;

        PackageManager packageManager = getPackageManager();
        return packageManager != null && intent.resolveActivity(packageManager) != null;
    }

    private boolean isPhotoPickerAvailable()
    {
        return Build.VERSION.SDK_INT >= 30;
    }

    private void importSelectedImages(Intent resultIntent)
    {
        LinkedHashSet<Uri> selectedUris = collectSelectedUris(resultIntent);
        if (selectedUris.isEmpty())
            return;

        File importDirectory = resolveImportDirectory();
        if (importDirectory == null)
            return;

        ArrayList<String> importedPaths = new ArrayList<>();

        for (Uri uri : selectedUris)
        {
            if (uri == null)
                continue;

            tryTakePersistablePermission(uri, resultIntent.getFlags());

            String importedPath = copyUriToImportDirectory(uri, importDirectory);
            if (importedPath != null && importedPath.length() > 0)
                importedPaths.add(importedPath);
        }

        if (importedPaths.isEmpty())
            return;

        MediaShareReceiverActivity.enqueuePendingImportedPaths(importedPaths);
        Log.i(TAG, "Imported " + importedPaths.size() + " image(s) from picker.");
    }

    private LinkedHashSet<Uri> collectSelectedUris(Intent resultIntent)
    {
        LinkedHashSet<Uri> selectedUris = new LinkedHashSet<>();
        if (resultIntent == null)
            return selectedUris;

        if (resultIntent.getClipData() != null)
        {
            for (int i = 0; i < resultIntent.getClipData().getItemCount(); i++)
            {
                if (resultIntent.getClipData().getItemAt(i) != null)
                    selectedUris.add(resultIntent.getClipData().getItemAt(i).getUri());
            }
        }

        selectedUris.add(resultIntent.getData());
        selectedUris.remove(null);
        return selectedUris;
    }

    private void tryTakePersistablePermission(Uri uri, int grantFlags)
    {
        if (uri == null || Build.VERSION.SDK_INT < 19)
            return;

        int readFlags = grantFlags & (Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
        if (readFlags == 0)
            return;

        try
        {
            getContentResolver().takePersistableUriPermission(uri, readFlags);
        }
        catch (Exception ignored)
        {
        }
    }

    private File resolveImportDirectory()
    {
        File root = getExternalFilesDir(null);
        if (root == null)
            root = getFilesDir();

        if (root == null)
        {
            Log.e(TAG, "Could not resolve an app-owned import directory.");
            return null;
        }

        File importDirectory = new File(root, "ImportedSharedMedia");
        if (!importDirectory.exists() && !importDirectory.mkdirs())
        {
            Log.e(TAG, "Could not create import directory: " + importDirectory.getAbsolutePath());
            return null;
        }

        return importDirectory;
    }

    private String copyUriToImportDirectory(Uri uri, File importDirectory)
    {
        ContentResolver resolver = getContentResolver();
        String displayName = queryDisplayName(resolver, uri);
        String fileName = buildUniqueFileName(importDirectory, displayName, resolver.getType(uri));
        File outputFile = new File(importDirectory, fileName);

        InputStream inputStream = null;
        OutputStream outputStream = null;

        try
        {
            inputStream = resolver.openInputStream(uri);
            if (inputStream == null)
            {
                Log.w(TAG, "Picker URI returned a null stream: " + uri);
                return null;
            }

            outputStream = new FileOutputStream(outputFile);
            byte[] buffer = new byte[16 * 1024];
            int bytesRead;

            while ((bytesRead = inputStream.read(buffer)) != -1)
                outputStream.write(buffer, 0, bytesRead);

            outputStream.flush();
            return outputFile.getAbsolutePath();
        }
        catch (Exception ex)
        {
            Log.e(TAG, "Failed to import picked image: " + uri, ex);
            if (outputFile.exists())
                //noinspection ResultOfMethodCallIgnored
                outputFile.delete();
            return null;
        }
        finally
        {
            try
            {
                if (inputStream != null)
                    inputStream.close();
            }
            catch (Exception ignored)
            {
            }

            try
            {
                if (outputStream != null)
                    outputStream.close();
            }
            catch (Exception ignored)
            {
            }
        }
    }

    private String queryDisplayName(ContentResolver resolver, Uri uri)
    {
        Cursor cursor = null;

        try
        {
            cursor = resolver.query(uri, new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null);
            if (cursor != null && cursor.moveToFirst())
            {
                int columnIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (columnIndex >= 0)
                {
                    String value = cursor.getString(columnIndex);
                    if (value != null && value.length() > 0)
                        return value;
                }
            }
        }
        catch (Exception ex)
        {
            Log.w(TAG, "Failed to query display name for picker URI: " + uri, ex);
        }
        finally
        {
            if (cursor != null)
                cursor.close();
        }

        String fallback = uri.getLastPathSegment();
        return (fallback == null || fallback.length() == 0) ? "picked-image" : fallback;
    }

    private String buildUniqueFileName(File importDirectory, String displayName, String mimeType)
    {
        String sanitizedName = sanitizeFileName(displayName, mimeType);
        String baseName = sanitizedName;
        String extension = "";
        int extensionIndex = sanitizedName.lastIndexOf('.');

        if (extensionIndex > 0 && extensionIndex < sanitizedName.length() - 1)
        {
            baseName = sanitizedName.substring(0, extensionIndex);
            extension = sanitizedName.substring(extensionIndex);
        }

        String candidate = sanitizedName;
        int suffix = 1;

        while (new File(importDirectory, candidate).exists())
        {
            candidate = baseName + "-" + suffix + extension;
            suffix++;
        }

        return candidate;
    }

    private String sanitizeFileName(String displayName, String mimeType)
    {
        String value = displayName != null ? displayName.trim() : "";
        if (value.length() == 0)
            value = "picked-image";

        value = value.replaceAll("[\\\\/:*?\"<>|]", "_");

        String extension = "";
        int extensionIndex = value.lastIndexOf('.');
        if (extensionIndex > 0 && extensionIndex < value.length() - 1)
            extension = value.substring(extensionIndex + 1);

        if (extension.length() == 0 && mimeType != null && mimeType.length() > 0)
        {
            String mimeExtension = MimeTypeMap.getSingleton().getExtensionFromMimeType(mimeType.toLowerCase(Locale.ROOT));
            if (mimeExtension != null && mimeExtension.length() > 0)
                value = value + "." + mimeExtension;
        }

        return value;
    }

    private void launchUnityActivity()
    {
        try
        {
            Intent launchIntent = getPackageManager().getLaunchIntentForPackage(getPackageName());
            if (launchIntent != null)
            {
                launchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
                startActivity(launchIntent);
            }
        }
        catch (Exception ex)
        {
            Log.w(TAG, "Failed to relaunch Unity activity after import.", ex);
        }
    }
}
