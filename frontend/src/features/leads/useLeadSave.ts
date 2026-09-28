import { useState } from "react";
import { useToast } from "../../components/ui";

/**
 * Saving from a lead popup: `saving` while the request runs, a success toast and `onSaved` when it
 * worked, and the server's message as an error toast when it did not. The popup stays open on a
 * failure, so nothing typed is lost.
 */
export function useLeadSave(onSaved: () => void) {
  const toast = useToast();
  const [saving, setSaving] = useState(false);

  const run = async <T,>(request: () => Promise<T>, success: string | ((result: T) => string)) => {
    setSaving(true);
    try {
      const result = await request();
      toast.success(typeof success === "string" ? success : success(result));
      onSaved();
    } catch (caught) {
      toast.error(caught instanceof Error ? caught.message : "That could not be saved. Try again.");
    } finally {
      setSaving(false);
    }
  };

  return { saving, run };
}
