package com.multiuserfullbody.mediaupload;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.util.Log;
import android.webkit.MimeTypeMap;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.Locale;

public class MediaShareReceiverActivity extends Activity
{
    private static final String TAG = "MediaShareReceiver";
    private static final String IMPORT_FOLDER_NAME = "ImportedSharedMedia";
    private static final Object PENDING_LOCK = new Object();
    private static final ArrayList<String> pendingImportedPaths = new ArrayList<>();

    public static String[] consumePendingImportedPaths()
    {
        synchronized (PENDING_LOCK)
        {
            if (pendingImportedPaths.isEmpty())
                return new String[0];

            String[] result = pendingImportedPaths.toArray(new String[0]);
            pendingImportedPaths.clear();
            return result;
        }
    }

    public static void enqueuePendingImportedPaths(ArrayList<String> importedPaths)
    {
        if (importedPaths == null || importedPaths.isEmpty())
            return;

        synchronized (PENDING_LOCK)
        {
            pendingImportedPaths.addAll(importedPaths);
        }
    }

    public static String getImportDirectoryPath()
    {
        try
        {
            Activity activity = UnityPlayer.currentActivity;
            if (activity == null)
                return "";

            File root = activity.getExternalFilesDir(null);
            if (root == null)
                root = activity.getFilesDir();

            if (root == null)
                return "";

            File importDirectory = new File(root, IMPORT_FOLDER_NAME);
            if (!importDirectory.exists() && !importDirectory.mkdirs())
                return "";

            return importDirectory.getAbsolutePath();
        }
        catch (Exception ex)
        {
            Log.w(TAG, "Failed to resolve the managed import directory path.", ex);
            return "";
        }
    }

    @Override
    protected void onCreate(Bundle savedInstanceState)
    {
        super.onCreate(savedInstanceState);
        handleIncomingShare(getIntent());
        launchUnityActivity();
        finish();
        overridePendingTransition(0, 0);
    }

    private void handleIncomingShare(Intent intent)
    {
        if (intent == null)
            return;

        ArrayList<String> importedPaths = importSharedImages(intent);
        if (importedPaths.isEmpty())
            return;

        enqueuePendingImportedPaths(importedPaths);

        Log.i(TAG, "Imported " + importedPaths.size() + " shared image(s) into app storage.");
    }

    private ArrayList<String> importSharedImages(Intent intent)
    {
        ArrayList<String> importedPaths = new ArrayList<>();
        LinkedHashSet<Uri> sharedUris = collectSharedUris(intent);
        File importDirectory = resolveImportDirectory();

        if (sharedUris.isEmpty() || importDirectory == null)
            return importedPaths;

        for (Uri uri : sharedUris)
        {
            if (uri == null)
                continue;

            String copiedPath = copyUriToImportDirectory(uri, importDirectory);
            if (copiedPath != null && copiedPath.length() > 0)
                importedPaths.add(copiedPath);
        }

        return importedPaths;
    }

    private LinkedHashSet<Uri> collectSharedUris(Intent intent)
    {
        LinkedHashSet<Uri> uris = new LinkedHashSet<>();
        String action = intent.getAction();

        if (Intent.ACTION_SEND.equals(action))
            addUri(uris, intent.getParcelableExtra(Intent.EXTRA_STREAM));
        else if (Intent.ACTION_SEND_MULTIPLE.equals(action))
        {
            ArrayList<Uri> streams = intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
            if (streams != null)
            {
                for (Uri streamUri : streams)
                    addUri(uris, streamUri);
            }
        }

        if (intent.getClipData() != null)
        {
            for (int i = 0; i < intent.getClipData().getItemCount(); i++)
                addUri(uris, intent.getClipData().getItemAt(i).getUri());
        }

        addUri(uris, intent.getData());
        return uris;
    }

    private void addUri(LinkedHashSet<Uri> uris, Uri uri)
    {
        if (uris == null || uri == null)
            return;

        uris.add(uri);
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

        File importDirectory = new File(root, IMPORT_FOLDER_NAME);
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
                Log.w(TAG, "Shared URI returned a null stream: " + uri);
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
            Log.e(TAG, "Failed to import shared image from URI: " + uri, ex);
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
            Log.w(TAG, "Failed to query display name for URI: " + uri, ex);
        }
        finally
        {
            if (cursor != null)
                cursor.close();
        }

        String fallback = uri.getLastPathSegment();
        if (fallback == null || fallback.length() == 0)
            fallback = "shared-image";

        return fallback;
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
            value = "shared-image";

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
        Intent launchIntent = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (launchIntent == null)
        {
            Log.e(TAG, "Could not resolve the Unity launch activity.");
            return;
        }

        launchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        startActivity(launchIntent);
    }
}
